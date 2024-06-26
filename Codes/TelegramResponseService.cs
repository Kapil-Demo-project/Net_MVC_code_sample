using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using DreamHouseSmartBot.Extensions;
using DreamHouseSmartBot.Database.Services;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using DreamHouseSmartBot.Configuration.Models;

namespace DreamHouseSmartBot.TelegramBot
{
    public class TelegramResponseService
    {
        private readonly TelegramBotClient _client;
        private readonly TelegramKeyboardLayoutService _keyboardLayoutService;
        private readonly TelegramUtils _telegramUtils;
        private readonly SamanDbService _samanDbService;
        private readonly StatisticsDbService _statisticsDbService;
        private readonly AppConfig _config;
        private readonly ILogger _logger;

        public TelegramResponseService(TelegramBotClient client, TelegramKeyboardLayoutService keyboardLayoutService, TelegramUtils telegramUtils,  SamanDbService samanDbService, StatisticsDbService statisticsDbService, IOptions<AppConfig> options, ILogger<TelegramResponseService> logger)
        {
            _client = client;
            _keyboardLayoutService = keyboardLayoutService;
            _telegramUtils = telegramUtils;
            _samanDbService = samanDbService;
            _statisticsDbService = statisticsDbService;
            _config = options.Value;
            _logger = logger;
        }

        public async Task SendHelpMessageAsync(long chatId)
        {
            try
            {
                await _client.SendTextMessageAsync(
                    chatId,
                    _config.TelegramBot.GuidText,
                    replyMarkup: _keyboardLayoutService.MainMenuLayout
                );
            }
            catch
            {
                // ignored
            }
        }

        public async Task SendMaintenanceModeMessageAsync(long chatId)
        {
            try
            {
                await _client.SendTextMessageAsync(
                    chatId,
                    _config.TelegramBot.Texts.MaintenanceMode
                );
            }
            catch
            {
                // ignored
            }
        }

        public async Task SendInvalidCommandMessageAsync(long chatId)
        {
            try
            {
                await _client.SendTextMessageAsync(
                    chatId,
                    _config.TelegramBot.Texts.InvalidCommand
                );

                await SendHelpMessageAsync(
                    chatId
                );
            }
            catch
            {
                // ignored
            }
        }

        public async Task SendNonExistingCommandMessageAsync(long chatId)
        {
            try
            {
                await _client.SendTextMessageAsync(
                    chatId,
                    _config.TelegramBot.Texts.NonExistingCommand
                );

                await SendHelpMessageAsync(
                    chatId
                );
            }
            catch
            {
                // ignored
            }
        }

        public async Task SendNonExistingProductCodesMessageAsync(long chatId)
        {
            try
            {
                await _client.SendTextMessageAsync(
                    chatId,
                    _config.TelegramBot.Texts.NonExistingProductCodes,
                    replyMarkup: _keyboardLayoutService.MainMenuLayout
                );
            }
            catch
            {
                // ignored
            }
        }

        public async Task SendInvalidRequiredQuantityOrMeterMessageAsync(long chatId,
            bool? isQuantityBased)
        {
            try
            {
                await _client.SendTextMessageAsync(
                    chatId,
                    !isQuantityBased.HasValue
                        ? _config.TelegramBot.Texts.InvalidRequiredQuantityOrMeter
                        : isQuantityBased.Value
                            ? _config.TelegramBot.Texts.InvalidRequiredQuantity
                            : _config.TelegramBot.Texts.InvalidRequiredMeter,
                    replyMarkup: _keyboardLayoutService.MainMenuLayout
                );
            }
            catch
            {
                // ignored
            }
        }

        public async Task SendLoadingMenuMessageAsync(long chatId)
        {
            try
            {
                await _client.SendTextMessageAsync(
                    chatId,
                    _config.TelegramBot.Texts.LoadingMenu
                );
            }
            catch
            {
                // ignored
            }
        }

        public async Task SendSearchingMessageAsync(long chatId, string productCode,
            decimal requiredQuantityOrMeter, string productType)
        {
            try
            {
                var searching = $"{_config.TelegramBot.Texts.Searching} {productCode}" +
                                (!string.IsNullOrEmpty(productType)
                                    ? $" از {_config.TelegramBot.Texts.ProductType} {_config.Database.ProductTypesToDescriptions[productType]}"
                                    : "") +
                                (requiredQuantityOrMeter > 0
                                    ? $" {(string.IsNullOrEmpty(productType) ? _config.TelegramBot.Texts.MeterOrQuantity : _config.Database.IsProductMeterBased(productType) ? _config.TelegramBot.Texts.Meter : _config.TelegramBot.Texts.Quantity)} {requiredQuantityOrMeter} {(string.IsNullOrEmpty(productType) ? _config.TelegramBot.Texts.MeterOrQuantityUnit : _config.Database.IsProductMeterBased(productType) ? _config.TelegramBot.Texts.MeterUnit : _config.TelegramBot.Texts.QuantityUnit)}"
                                    : "") +
                                "...";

                await _client.SendTextMessageAsync(
                    chatId,
                    searching
                );
            }
            catch
            {
                // ignored
            }
        }

        public async Task SendQueryResponseMessageAsync(Message message, string productCode,
            decimal requiredQuantityOrMeter, string productType, bool isAdminCommand, bool showMenu = false,
            bool showSeaching = true)
        {
            productCode = productCode.ToEnglishDigits();

            if (_config.TelegramBot.ShowSearchingMessage && showSeaching)
            {
                await SendSearchingMessageAsync(
                    message.Chat.Id,
                    productCode,
                    requiredQuantityOrMeter,
                    productType
                );
            }

            var productStats = await _samanDbService.GetStatsAsync(productCode, productType);
            if (productStats.Any())
            {
                foreach (var stat in productStats
                            .OrderBy(p => p.Code, new ProductCodeComparator())
                            .ThenBy(p => p.Code.ToLower())
                            .Take(_config.TelegramBot.MaxResultsToShow))
                {
                    var response = isAdminCommand
                        ? stat.ToString()
                        : stat.ToShortString(requiredQuantityOrMeter);
                    try
                    {
                        await _client.SendTextMessageAsync(
                            message.Chat.Id,
                            response,
                            replyMarkup: showMenu
                                ? _keyboardLayoutService.MainMenuLayout
                                : null
                        );
                    }
                    catch
                    {
                        // ignored
                    }

                    if (_config.Database.SendQueryStatsToStatisticsDatabase)
                    {
                        // do not await it
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await _statisticsDbService.SendProductQueryStatAsync(
                                    message,
                                    stat,
                                    requiredQuantityOrMeter
                                );
                                _logger.LogInformation($"Sent stat for the query of user {message.From.Id} ({_telegramUtils.GetSenderIdentifier(message)}), chat {message.Chat.Id}");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError("Error when adding stat to statistics db:\n" + ex.ToString());
                            }
                        });
                    }
                }
            }
            else
            {
                try
                {
                    await _client.SendTextMessageAsync(
                        message.Chat.Id,
                        _config.TelegramBot.Texts.NoItemFound,
                        replyMarkup: _keyboardLayoutService.MainMenuLayout
                    );
                }
                catch
                {
                    // ignored
                }
            }
        }

        public async Task SendInvalidAdminPasswordMessageAsync(long chatId)
        {
            try
            {
                await _client.SendTextMessageAsync(
                    chatId,
                    _config.TelegramBot.Texts.InvalidAdminPassword,
                    replyMarkup: _keyboardLayoutService.MainMenuLayout
                );
            }
            catch
            {
                // ignored
            }
        }

        public async Task SendInvalidReportPasswordMessageAsync(long chatId)
        {
            try
            {
                await _client.SendTextMessageAsync(
                    chatId,
                    _config.TelegramBot.Texts.InvalidReportPassword,
                    replyMarkup: _keyboardLayoutService.MainMenuLayout
                );
            }
            catch
            {
                // ignored
            }
        }

        public async Task SendInvalidStatisticsPasswordMessageAsync(long chatId)
        {
            try
            {
                await _client.SendTextMessageAsync(
                    chatId,
                    _config.TelegramBot.Texts.InvalidStatisticsPassword,
                    replyMarkup: _keyboardLayoutService.MainMenuLayout
                );
            }
            catch
            {
                // ignored
            }
        }

        public async Task SendProductTypeCodesMessageAsync(long chatId, string productType,
            int pageNumber, int messageId = 0)
        {
            try
            {
                var messageText = $"One {_config.TelegramBot.Texts.Product} from {_config.TelegramBot.Texts.ProductType} {_config.Database.ProductTypesToDescriptions[productType]} {_config.TelegramBot.Texts.Choose} ({_config.TelegramBot.Texts.Page} {pageNumber + 1}):";
                var replyMarkup = _keyboardLayoutService.GetProductTypeCodesMenuLayout(productType, pageNumber);

                if (messageId == 0)
                {
                    await _client.SendTextMessageAsync(
                        chatId,
                        messageText,
                        replyMarkup: replyMarkup
                    );
                }
                else
                {
                    await _client.EditMessageTextAsync(
                        chatId,
                        messageId,
                        messageText,
                        replyMarkup: replyMarkup
                    );
                }
            }
            catch
            {
                await SendNonExistingProductCodesMessageAsync(
                    chatId
                );
            }
        }

        public async Task SendQuantityOrMeterQueryMessageAsync(long chatId, string productCode,
            string productType)
        {
            try
            {
                await _client.SendTextMessageAsync(
                    chatId,
                    string.Format(
                        _config.Database.IsProductMeterBased(productType)
                            ? _config.TelegramBot.MeterQueryMessageFormat
                            : _config.TelegramBot.QuantityQueryMessageFormat, productCode,
                        _config.Database.ProductTypesToDescriptions[productType]),
                    replyMarkup: new ForceReplyMarkup()
                );
            }
            catch
            {
                // ignored
            }
        }

        public async Task SendReportMessageAsync(long chatId)
        {
            try
            {
                await _client.SendTextMessageAsync(
                    chatId,
                    $"{_config.TelegramBot.Texts.PreparingReport} (with amount less than {_config.Database.MinimumRequiredWarehouseQauntityToShowTheResult})..."
                );

                var excelFilePath = await _samanDbService.CreateExcelReportForLimitedProductsAsync();

                await _client.SendDocumentAsync(
                    chatId,
                    new InputMedia(new FileStream(excelFilePath, FileMode.Open, FileAccess.Read), "Report.xlsx"),
                    replyMarkup: _keyboardLayoutService.MainMenuLayout
                );
            }
            catch
            {
                // ignored
            }
        }

        public async Task SendStatisticsMessageAsync(long chatId, string startDate, string endDate, string itemsCount)
        {
            try
            {
                await _client.SendTextMessageAsync(
                    chatId,
                    _config.TelegramBot.Texts.PreparingStatistics
                );

                var excelFilePath = await _statisticsDbService.CreateExcelReportForQueryDataAsync(startDate, endDate, itemsCount);

                if (excelFilePath != null)
                {
                    await _client.SendDocumentAsync(
                        chatId,
                        new InputMedia(new FileStream(excelFilePath, FileMode.Open, FileAccess.Read), "Statistics.xlsx"),
                        replyMarkup: _keyboardLayoutService.MainMenuLayout
                    );
                }
                else
                {
                    await _client.SendTextMessageAsync(
                        chatId,
                        _config.TelegramBot.Texts.NoStatisticsFound
                    );
                }
            }
            catch
            {
                // ignored
            }
        }
    }

    class ProductCodeComparator : IComparer<string>
    {
        public int Compare(string x, string y)
        {
            var indexX = Array.FindIndex(x.ToCharArray(), t => !char.IsNumber(t));
            var indexY = Array.FindIndex(y.ToCharArray(), t => !char.IsNumber(t));
            return indexY.CompareTo(indexX);
        }
    }
}