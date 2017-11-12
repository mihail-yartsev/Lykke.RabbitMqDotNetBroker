﻿using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Common;
using Lykke.RabbitMqBroker;
using Lykke.RabbitMqBroker.Subscriber;
using NUnit.Framework;
using RabbitMQ.Client;

namespace RabbitMqBrokerTests
{
    [TestFixture(Category = "Integration")]
    internal sealed class RabbitMqSubsriberTest : RabbitMqPublisherSubscriberBaseTest
    {

        private RabbitMqSubscriber<string> _subscriber;

        [SetUp]
        public void SetUp()
        {
            _subscriber = new RabbitMqSubscriber<string>(_settings, new DefaultErrorHandlingStrategy(Log, _settings))
                .SetConsole(_console)
                .SetLogger(Log)
                .CreateDefaultBinding()
                .SetMessageDeserializer(new DefaultStringDeserializer());
        }

        [Test]
        public void SuccessfulPath()
        {
            const string expected = "GetDefaultHost message";

            string result = null;
            SetupNormalQueue();
            var completeLock = new ManualResetEventSlim(false);
            var handler = new Func<string, Task>(s =>
            {
                result = s;
                completeLock.Set();
                return Task.CompletedTask;
            });
            _subscriber.Subscribe(handler);

            _subscriber.Start();

            PublishToExchange(expected);

            completeLock.Wait();
            Assert.That(result, Is.EqualTo(expected));
        }


        [Test]
        public void ShouldUseDeadLetterQueueOnException()
        {
            _subscriber = new RabbitMqSubscriber<string>(_settings, new DeadQueueErrorHandlingStrategy(Log, _settings))
                .SetLogger(Log)
                .CreateDefaultBinding()
                .SetMessageDeserializer(new DefaultStringDeserializer());

            const string expected = "GetDefaultHost message";

            SetupNormalQueue();
            PublishToExchange(expected);

            var completeLock = new ManualResetEventSlim(false);
            var handler = new Func<string, Task>(s =>
            {
                completeLock.Set();
                throw new Exception();
            });
            _subscriber.Subscribe(handler);
            _subscriber.Start();

            completeLock.Wait();

            var result = ReadFromQueue(PoisonQueueName);

            Assert.That(result, Is.EqualTo(expected));
        }





        [TearDown]
        public void TearDown()
        {
            ((IStopable)_subscriber).Stop();
        }


        private void PublishToExchange(params string[] messages)
        {
            var factory = new ConnectionFactory { Uri = RabbitConnectionString };

            using (var connection = factory.CreateConnection())
            using (var channel = connection.CreateModel())
            {
                foreach (var message in messages)
                {
                    var body = Encoding.UTF8.GetBytes(message);
                    channel.BasicPublish(_settings.ExchangeName, _settings.RoutingKey, body: body);
                }
            }
        }

    }
}

