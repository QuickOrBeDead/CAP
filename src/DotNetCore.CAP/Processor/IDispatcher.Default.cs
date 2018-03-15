﻿using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using DotNetCore.CAP.Models;
using Microsoft.Extensions.Logging;

namespace DotNetCore.CAP.Processor
{
    public class Dispatcher : IDispatcher, IDisposable
    {
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly ISubscriberExecutor _executor;
        private readonly ILogger<Dispatcher> _logger;

        private readonly BlockingCollection<CapPublishedMessage> _publishedMessageQueue =
            new BlockingCollection<CapPublishedMessage>(new ConcurrentQueue<CapPublishedMessage>());

        private readonly BlockingCollection<CapReceivedMessage> _receivedMessageQueue =
            new BlockingCollection<CapReceivedMessage>(new ConcurrentQueue<CapReceivedMessage>());

        private readonly IPublishMessageSender _sender;

        public Dispatcher(ILogger<Dispatcher> logger,
            IPublishMessageSender sender,
            ISubscriberExecutor executor)
        {
            _logger = logger;
            _sender = sender;
            _executor = executor;

            Task.Factory.StartNew(Sending);
            Task.Factory.StartNew(Processing);
        }

        public void EnqueuToPublish(CapPublishedMessage message)
        {
            _publishedMessageQueue.Add(message);
        }

        public void EnqueuToExecute(CapReceivedMessage message)
        {
            _receivedMessageQueue.Add(message);
        }

        public void Dispose()
        {
            _cts.Cancel();
        }

        private void Sending()
        {
            try
            {
                while (!_publishedMessageQueue.IsCompleted)
                {
                    if (_publishedMessageQueue.TryTake(out var message, 100))
                    {
                        try
                        {
                            _sender.SendAsync(message);
                        }
                        catch (Exception ex)
                        {
                            _logger.ExceptionOccuredWhileExecuting(message.Name, ex);
                        }
                    }
                }

                //foreach (var message in _publishedMessageQueue..GetConsumingEnumerable(_cts.Token))
                //    try
                //    {
                //        _sender.SendAsync(message);
                //    }
                //    catch (Exception ex)
                //    {
                //        _logger.ExceptionOccuredWhileExecuting(message.Name, ex);
                //    }
            }
            catch (OperationCanceledException)
            {
                // expected
            }
        }

        private void Processing()
        {
            try
            {
                foreach (var message in _receivedMessageQueue.GetConsumingEnumerable(_cts.Token))
                    try
                    {
                        _executor.ExecuteAsync(message);
                    }
                    catch (Exception ex)
                    {
                        _logger.ExceptionOccuredWhileExecuting(message.Name, ex);
                    }
            }
            catch (OperationCanceledException)
            {
                // expected
            }
        }
    }
}