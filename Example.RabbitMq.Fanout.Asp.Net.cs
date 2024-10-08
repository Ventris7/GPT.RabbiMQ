// Интерфейс RabbitMQ сервиса
public interface IRabbitMqService
{
    void SendMessage(string message, string queueName);
    void Subscribe(string queueName, Action<string> handleMessage);
    void SendMessageFanout(string message, string exchangeName);
    void SubscribeFanout(string exchangeName, Action<string> handleMessage);
}

// Реализация RabbitMQ сервиса с поддержкой fanout
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using Microsoft.Extensions.Configuration;

public class RabbitMqService : IRabbitMqService
{
    private readonly IConnection _connection;
    private readonly IModel _channel;

    public RabbitMqService(IConfiguration configuration)
    {
        var factory = new ConnectionFactory()
        {
            HostName = configuration["RabbitMq:HostName"],
            UserName = configuration["RabbitMq:UserName"],
            Password = configuration["RabbitMq:Password"]
        };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();
    }

    public void SendMessage(string message, string queueName)
    {
        _channel.QueueDeclare(queue: queueName, durable: false, exclusive: false, autoDelete: false, arguments: null);
        var body = Encoding.UTF8.GetBytes(message);
        _channel.BasicPublish(exchange: "", routingKey: queueName, basicProperties: null, body: body);
    }

    public void Subscribe(string queueName, Action<string> handleMessage)
    {
        _channel.QueueDeclare(queue: queueName, durable: false, exclusive: false, autoDelete: false, arguments: null);
        var consumer = new EventingBasicConsumer(_channel);
        consumer.Received += (model, ea) =>
        {
            var body = ea.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);
            handleMessage(message);
        };
        _channel.BasicConsume(queue: queueName, autoAck: true, consumer: consumer);
    }

    // Функция для отправки сообщения через exchange типа fanout
    public void SendMessageFanout(string message, string exchangeName)
    {
        _channel.ExchangeDeclare(exchange: exchangeName, type: ExchangeType.Fanout);
        var body = Encoding.UTF8.GetBytes(message);
        _channel.BasicPublish(exchange: exchangeName, routingKey: "", basicProperties: null, body: body);
    }

    // Функция для подписки на exchange типа fanout
    public void SubscribeFanout(string exchangeName, Action<string> handleMessage)
    {
        _channel.ExchangeDeclare(exchange: exchangeName, type: ExchangeType.Fanout);
        var queueName = _channel.QueueDeclare().QueueName; // Создаем временную очередь
        _channel.QueueBind(queue: queueName, exchange: exchangeName, routingKey: "");

        var consumer = new EventingBasicConsumer(_channel);
        consumer.Received += (model, ea) =>
        {
            var body = ea.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);
            handleMessage(message);
        };
        _channel.BasicConsume(queue: queueName, autoAck: true, consumer: consumer);
    }
}
