using AutoMapper;
using Core.Db.Helpers;
using Core.Db.Models;
using Core.Db.Mongo;
using Core.Helpers.Extensions;
using Core.Helpers.Formatting;
using Core.Helpers.General;
using Core.Helpers.GeneralViewModels;
using Core.Helpers.Inventory;
using Core.Helpers.Permissions;
using Core.Helpers.Querying.Exporting;
using Core.Helpers.Translations;
using ExportService.Export.Exporters.Forms;
using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace ExportService.Export.BaseExporters
{
    public abstract class MongoExporterBase<TViewModel> : BaseExporter
        where TViewModel : IClpEntityContainer
    {
        protected readonly ITranslationsHelper<TViewModel> _translationsHelper;
        protected readonly IMapper _mapper;
        protected readonly IMongoFactory _mongoFactory;
        protected readonly IMongoCollection<BsonDocument> _mongoCollection;
        protected readonly IJsonConverter _jsonConverter;
        protected readonly IManualInventoryHelper _manualInventoryHelper;
        protected readonly IEntityFormattingHelper<TViewModel> _entityFormattingHelper;
        protected readonly IPermissionsHelper _permissionsHelper;
        protected readonly IMongoExportQueryBuilderFactory _mongoQueryBuilderFactory;
        protected readonly IMongoFormsCsvGenerator _mongoFormsCsvGenerator;
        protected readonly ICaseLevelPermissionsHelper _clpHelper;

        protected ProjectionDefinition<BsonDocument> _hiddenFieldsProjection;
        protected FilterDefinition<BsonDocument> _queryFilter;
        protected Dictionary<BsonDocument, TViewModel> _entitiesToExport;
        protected int _completionCount = 0;
        protected bool _restrictMediaToOwner;

        // for testing
        protected SortDefinition<BsonDocument> _sort;


        public MongoExporterBase(IMapper mapper,
                                    IMongoFactory mongoFactory,
                                    ITranslationsHelper<TViewModel> translationsHelper,
                                    IJsonConverter jsonConverter,
                                    IManualInventoryHelper manualInventoryHelper,
                                    IEntityFormattingHelper<TViewModel> entityFormattingHelper,
                                    IPermissionsHelper permissionsHelper,
                                    IMongoExportQueryBuilderFactory mongoQueryBuilderFactory,
                                    IMongoFormsCsvGenerator mongoFormsCsvGenerator,
                                    string collectionName,
                                    ICaseLevelPermissionsHelper clpHelper)
        {
            _mapper = mapper;
            _mongoFactory = mongoFactory;
            _translationsHelper = translationsHelper;
            _mongoCollection = _mongoFactory.GetCollection<BsonDocument>(collectionName);
            _jsonConverter = jsonConverter;
            _manualInventoryHelper = manualInventoryHelper;
            _entityFormattingHelper = entityFormattingHelper;
            _permissionsHelper = permissionsHelper;
            _mongoQueryBuilderFactory = mongoQueryBuilderFactory;
            _mongoFormsCsvGenerator = mongoFormsCsvGenerator;
            _clpHelper = clpHelper;
        }

        protected void SetupHiddenFieldsAndFormattingHelper()
        {
            _hiddenFieldsProjection = GetHiddenFieldsProjection();
            _entityFormattingHelper.SetOrganizationAndUser(_exportJob.OrganizationId, _exportJob.UserId);
        }

        private void SetSort(bool orderByAsc, string orderBy, bool? thenOrderByAsc, string thenOrderBy)
        {
            var builder = Builders<BsonDocument>.Sort;

            // Primary Sorting (OrderBy)
            if (CanSortByField(orderBy))
            {
                _sort = orderByAsc ? builder.Ascending(orderBy) : builder.Descending(orderBy);
            }

            // Secondary Sorting (optional - ThenOrderBy)
            if (!string.IsNullOrWhiteSpace(thenOrderBy) && thenOrderByAsc.HasValue && CanSortByField(thenOrderBy))
            {
                if (_sort != null)
                {
                    _sort = thenOrderByAsc.Value
                        ? _sort.Ascending(thenOrderBy)
                        : _sort.Descending(thenOrderBy);
                }
                else
                {
                    _sort = thenOrderByAsc.Value
                        ? builder.Ascending(thenOrderBy)
                        : builder.Descending(thenOrderBy);
                }
            }
        }

        protected override async Task CreateQuery()
        {
            var searchVm = _jsonConverter.GetSearchViewModel<SearchViewModel>(_exportJob.Query);
            searchVm.ScrubSearchPhoneNumbers();
            SetSort(searchVm.OrderByAsc, searchVm.OrderBy, searchVm.ThenOrderByAsc, searchVm.ThenOrderBy);

            var mongoQueryBuilder = _mongoQueryBuilderFactory.GetQueryBuilder(_exportJob.ModelType);
            _queryFilter = mongoQueryBuilder.CreateQuery(searchVm, _exportJob.OrganizationId);

            if (_exportJob.SubsetTypeSelection != (int)SubsetTypeEnum.All)
            {
                _queryFilter = await _manualInventoryHelper.AdjustFilterToReturnSubsetOfResults(_queryFilter, _exportJob);
            }
        }

        protected override async Task ExportEntities()
        {
            _restrictMediaToOwner = _exportJob.OfficeId.HasValue
                ? _permissionsHelper.RestrictMediaByOwner(_exportJob.OrganizationId, _exportJob.OfficeId.Value, _exportJob.UserId)
                : false;

            var totalRecordsCount = await _mongoCollection.CountDocumentsAsync(_queryFilter);
            await ReportTotalRecordsCount(totalRecordsCount);

            await PrepareExportEntities();

            if (!JobCanceled)
            {
                await BuildResultsToFile();
            }

            if (!JobCanceled)
            {
                await WriteEntitiesToOutStream();
            }

            _exportedEntitiesCount = _entitiesToExport.Count;
        }

        


        private async Task PrepareExportEntities()
        {
            var options = new FindOptions<BsonDocument>
            {
                NoCursorTimeout = true,
                BatchSize = BatchSize
            };

            if (_hiddenFieldsProjection != null)
            {
                options.Projection = _hiddenFieldsProjection;
            }

            if (_sort != null)
            {
                options.Sort = _sort;
            }

            using (var cursor = await _mongoCollection.FindAsync(_queryFilter, options))
            {
                await GetAndFormatEntities(cursor);
            }
        }

        private async Task GetAndFormatEntities(IAsyncCursor<BsonDocument> cursor)
        {
            _entitiesToExport = new Dictionary<BsonDocument, TViewModel>();

            // Prepare entities
            while (await cursor.MoveNextAsync())
            {
                var batch = cursor.Current;
                var batchVMs = new List<TViewModel>(BatchSize);

                foreach (var document in batch)
                {
                    var entityViewModel = _mapper.Map<TViewModel>(document);

                    _entitiesToExport.Add(document, entityViewModel);
                    batchVMs.Add(entityViewModel);

                    _completionCount++;

                    await ReportProgress(_completionCount);

                    if (JobCanceled)
                    {
                        return;
                    }
                }

                // Case-Level Permissions
                if (!_hasAdminAccess)
                {
                    _clpHelper.PopulateBase(_exportJob.OrganizationId);
                    await _clpHelper.StripInaccessibleEntities(batchVMs, false, _exportJob.UserId);
                }

                // Formatting entities
                await _entityFormattingHelper.FormatEntities(batchVMs);
            }
        }

        private async Task BuildResultsToFile()
        {
            foreach (var entityToExport in _entitiesToExport)
            {
                await ProcessExportedEntity(entityToExport.Value, entityToExport.Key);

                // Report the progress
                _completionCount++;
                await ReportProgress(_completionCount);

                if (JobCanceled)
                {
                    return;
                }
            }
        }

        protected abstract Task WriteEntitiesToOutStream();

        protected abstract Task ProcessExportedEntity(TViewModel entity, BsonDocument bsonEntity);

        protected virtual string GetNoAccessMessage(TViewModel entity) =>
            _translationsHelper.GetOrgTranslation("ITEM.ITEM_CANNOT_BE_DISPLAYED_NO_ACESS", _exportJob.OrganizationId);

        protected abstract ProjectionDefinition<BsonDocument> GetHiddenFieldsProjection();
        protected abstract bool CanSortByField(string orderBy);
    }
}
