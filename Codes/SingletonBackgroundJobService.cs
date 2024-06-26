using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Blt.RemoteManagement.REST.BackgroundServices
{
    public class SingletonBackgroundJobService
    {
        private readonly SingletonBackgroundJobConfig _jobConfig;
        private readonly IMemoryCache _memoryCache;
        private readonly ILogger<SingletonBackgroundJobService> _logger;

        public SingletonBackgroundJobService(SingletonBackgroundJobConfig jobConfig, IMemoryCache memoryCache, ILogger<SingletonBackgroundJobService> logger)
        {
            _jobConfig = jobConfig;
            _memoryCache = memoryCache;
            _logger = logger;
        }

        public SingletonBackgroundJobStatus LastStatus => _memoryCache.TryGetValue(_statusKey, out SingletonBackgroundJobStatus status)
            ? status
            : new SingletonBackgroundJobStatus(SingletonBackgroundJobStatusType.NeverStarted);

        public bool IsRunning => LastStatus.Type == SingletonBackgroundJobStatusType.InProgress;

        /// <summary>
        /// This will return true only if the task is ran AND finished successfully
        /// </summary>
        public bool LastRunFinishedSuccessfully => LastStatus.Type == SingletonBackgroundJobStatusType.Successful;

        public string LastMessage => LastStatus.Message;

        public bool Start(out string message)
        {
            if (IsRunning)
            {
                message = $"Job '{_jobConfig.Title}' is already started.";
                _logger.LogWarning(message);
                return false;
            }
            else
            {
                var source = new CancellationTokenSource();
                _memoryCache.Set(_tokenKey, source);
                Task.Run(async () =>
                {
                    try
                    {
                        await _jobConfig.DoWorkFunction(source.Token);
                        var message = $"Job '{_jobConfig.Title}' done.";
                        _logger.LogInformation(message);
                        _memoryCache.Remove(_tokenKey);
                        if (!source.IsCancellationRequested)
                        {
                            _memoryCache.Set(_statusKey, new SingletonBackgroundJobStatus(SingletonBackgroundJobStatusType.Successful, message));
                        }
                    }
                    catch (Exception ex)
                    {
                        var message = $"Error while running job '{_jobConfig.Title}': {ex.Message}";
                        _logger.LogError(ex, message);
                        _memoryCache.Remove(_tokenKey);
                        _memoryCache.Set(_statusKey, new SingletonBackgroundJobStatus(SingletonBackgroundJobStatusType.Failed, message));
                        source.Cancel();
                    }
                });
                message = $"Job '{_jobConfig.Title}' started.";
                _logger.LogInformation(message);
                _memoryCache.Set(_statusKey, new SingletonBackgroundJobStatus(SingletonBackgroundJobStatusType.InProgress, message));
                return true;
            }
        }

        public bool Stop(out string message)
        {
            if (IsRunning && _memoryCache.TryGetValue(_tokenKey, out CancellationTokenSource source))
            {
                _memoryCache.Remove(_tokenKey);
                source.Cancel();
                message = $"Job '{_jobConfig.Title}' stopped.";
                _logger.LogInformation(message);
                _memoryCache.Set(_statusKey, new SingletonBackgroundJobStatus(SingletonBackgroundJobStatusType.Stopped, message));
                return true;
            }
            else
            {
                message = $"Job '{_jobConfig.Title}' is not running.";
                _logger.LogWarning(message);
                return false;
            }
        }

        private object _tokenKey => _jobConfig.UniqueIdentifier;
        private object _statusKey => $"{_tokenKey}_status";
    }

    public class SingletonBackgroundJobConfig
    {
        public object UniqueIdentifier { get; init; }
        public string Title { get; init; }
        public Func<CancellationToken, Task> DoWorkFunction { get; set; }
    }

    public record SingletonBackgroundJobStatus(SingletonBackgroundJobStatusType Type, string Message = null);

    public enum SingletonBackgroundJobStatusType
    {
        NeverStarted, // not run yet
        InProgress, // still working
        Stopped, // stopped manually
        Failed, // failed
        Successful // done
    }
}
