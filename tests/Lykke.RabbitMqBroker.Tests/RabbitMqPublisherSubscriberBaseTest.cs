using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Common.Log;
using Lykke.RabbitMqBroker.Publisher;
using Lykke.RabbitMqBroker.Subscriber;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using NUnit.Framework;
using RabbitMQ.Client;

namespace RabbitMqBrokerTests
{
    using System.Collections;
    using System.Collections.Concurrent;
    using System.Threading;

    [TestFixture]
    public abstract class RabbitMqPublisherSubscriberBaseTest
    {
        protected IConnectionFactory _factory;
        protected RabbitMqSubscriptionSettings _settings;
        protected IConsole _console;
        protected ILog Log;
        protected string RabbitConnectionString;

        protected const string ExchangeName = "TestExchange";
        protected const string QueueName = "TestQueue";
        protected const string DeadLetterExchangeName = ExchangeName + "-DL";
        protected const string PoisonQueueName = QueueName + "-poison";


        protected class TestMessageSerializer : IRabbitMqSerializer<string>
        {
            public byte[] Serialize(string model)
            {
                return Encoding.UTF8.GetBytes(model);
            }
        }

        [SetUp]
        public void SetUpBase()
        {
            _console = new ConsoleLWriter(Console.Write);
            Log = Substitute.For<ILog>();
            ReadConfiguration();


            _settings = new RabbitMqSubscriptionSettings
            {
                ConnectionString = RabbitConnectionString,
                DeadLetterExchangeName = DeadLetterExchangeName,
                ExchangeName = ExchangeName,
                IsDurable = true,
                QueueName = QueueName,
                RoutingKey = "RoutingKey"
            };

            _factory = new ConnectionFactory { Uri = RabbitConnectionString };

            EnsureRabbitInstalledAndRun();
        }

        private void ReadConfiguration()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false)
                .AddJsonFile("appsettings.Development.json", optional: true);
            var config = builder.Build();
            RabbitConnectionString = config["RabbitMqConnectionString"];
        }

        private void EnsureRabbitInstalledAndRun()
        {
            try
            {
                using (var connection = _factory.CreateConnection())
                {

                }
            }
            catch (Exception e)
            {
                _console.WriteLine(e.Message);

                _console.WriteLine("The rabbitmq server either not installed or not started");
                Assert.Inconclusive("The rabbitmq server either not installed or not started");
            }

        }

        protected string ReadFromQueue(string queueName = QueueName, bool ack = true)
        {

            using (var connection = _factory.CreateConnection())
            using (var channel = connection.CreateModel())
            {
                var consumer = new QueueingBasicConsumer(channel);
                channel.BasicConsume(queueName, false, consumer);
                if (consumer.Queue.Dequeue(1000, out var eventArgs))
                {

                    if (ack)
                    {
                        channel.BasicAck(eventArgs.DeliveryTag, false);
                    }
                    else
                    {
                        channel.BasicReject(eventArgs.DeliveryTag, false);
                    }

                    return Encoding.UTF8.GetString(eventArgs.Body);
                }
                return string.Empty;
            }
        }

        protected void SetupPoisonQueue()
        {
            using (var connection = _factory.CreateConnection())
            using (var channel = connection.CreateModel())
            {
                channel.ExchangeDeclare(DeadLetterExchangeName, "direct", durable: true, autoDelete: false);
                channel.QueueDeclare(PoisonQueueName, autoDelete: false, durable: true, exclusive: false);
                channel.QueueBind(PoisonQueueName, DeadLetterExchangeName, _settings.RoutingKey);
            }
        }

        protected void SetupNormalQueue()
        {
            using (var connection = _factory.CreateConnection())
            using (var channel = connection.CreateModel())
            {
                var args = new Dictionary<string, object>
                {
                    {"x-dead-letter-exchange", DeadLetterExchangeName},
                };
                channel.ExchangeDeclare(ExchangeName, "fanout", true, false);
                channel.QueueDeclare(QueueName, autoDelete: false, exclusive: false, durable: _settings.IsDurable, arguments: args);
                channel.QueueBind(QueueName, ExchangeName, _settings.RoutingKey);
            }
        }

        protected class TestBuffer : IPublisherBuffer
        {
            private readonly BlockingCollection<byte[]> _collection = new BlockingCollection<byte[]>();

            public readonly ManualResetEventSlim Gate = new ManualResetEventSlim(false);

            public IEnumerator<byte[]> GetEnumerator()
            {
                return ((IEnumerable<byte[]>)_collection).GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }

            public void Dispose()
            {
                _collection.Dispose();
            }

            public int Count => _collection.Count;

            public void Enqueue(byte[] message, CancellationToken cancelationToken)
            {
                _collection.Add(message);
            }

            public byte[] Dequeue(CancellationToken cancelationToken)
            {
                Gate.Wait();
                return _collection.Take(cancelationToken);
            }
        }
    }
}
