// Интерфейс RabbitMQ сервиса
public interface IRabbitMqService
{
    void SendMessage(string message, string queueName);
    void Subscribe(string queueName, Action<string> handleMessage);
}

// Реализация RabbitMQ сервиса с использованием конфигурации
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
}

// Интерфейс TaskProcessingService
public interface ITaskProcessingService
{
    void StartProcessing(Func<string, Task> taskHandler);
    string GetLastTaskStatus();
}

// Реализация TaskProcessingService с BackgroundService
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

public class TaskProcessingService : BackgroundService, ITaskProcessingService
{
    private readonly IRabbitMqService _rabbitMqService;
    private readonly ILogger<TaskProcessingService> _logger;
    private Func<string, Task> _taskHandler;

    private string _lastTaskStatus = "No task processed yet";

    public TaskProcessingService(IRabbitMqService rabbitMqService, ILogger<TaskProcessingService> logger)
    {
        _rabbitMqService = rabbitMqService;
        _logger = logger;
    }

    public void StartProcessing(Func<string, Task> taskHandler)
    {
        _taskHandler = taskHandler;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_taskHandler == null)
        {
            throw new InvalidOperationException("Task handler is not set. Call StartProcessing() first.");
        }

        _rabbitMqService.Subscribe("tasks_queue", async message =>
        {
            _logger.LogInformation($"Received task: {message}");

            try
            {
                // Выполнение задачи через переданный обработчик
                await _taskHandler(message);
                
                _lastTaskStatus = $"Task {message} completed successfully";
                _rabbitMqService.SendMessage($"Task {message} completed successfully", "notifications_queue");

                _logger.LogInformation(_lastTaskStatus);
            }
            catch (Exception ex)
            {
                _lastTaskStatus = $"Task {message} failed: {ex.Message}";
                _rabbitMqService.SendMessage($"Task {message} failed: {ex.Message}", "notifications_queue");

                _logger.LogError(ex, _lastTaskStatus);
            }
        });

        return Task.CompletedTask;
    }

    public string GetLastTaskStatus()
    {
        return _lastTaskStatus;
    }
}

// Пример контроллера с простой логикой обработки задачи
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public class SimpleTaskController : ControllerBase
{
    private readonly ITaskProcessingService _taskProcessingService;

    public SimpleTaskController(ITaskProcessingService taskProcessingService)
    {
        _taskProcessingService = taskProcessingService;

        // Передаем простую логику обработки задачи
        _taskProcessingService.StartProcessing(async task =>
        {
            // Простая обработка задачи (например, симуляция задержки)
            await Task.Delay(2000);
            Console.WriteLine($"Processed task: {task}");
        });
    }

    [HttpGet("status")]
    public IActionResult GetTaskStatus()
    {
        var status = _taskProcessingService.GetLastTaskStatus();
        return Ok(status);
    }
}

// Пример контроллера с расширенной логикой обработки задачи
[ApiController]
[Route("api/[controller]")]
public class AdvancedTaskController : ControllerBase
{
    private readonly ITaskProcessingService _taskProcessingService;

    public AdvancedTaskController(ITaskProcessingService taskProcessingService)
    {
        _taskProcessingService = taskProcessingService;

        // Передаем более сложную логику обработки задачи
        _taskProcessingService.StartProcessing(async task =>
        {
            if (task.Contains("error"))
            {
                throw new Exception("Simulated task error");
            }

            // Симуляция сложной обработки задачи
            await Task.Delay(5000);
            Console.WriteLine($"Advanced processing of task: {task}");
        });
    }

    [HttpGet("status")]
    public IActionResult GetTaskStatus()
    {
        var status = _taskProcessingService.GetLastTaskStatus();
        return Ok(status);
    }
}

// Конфигурация в Program.cs
var builder = WebApplication.CreateBuilder(args);

// Добавляем сервисы
builder.Services.AddSingleton<IRabbitMqService, RabbitMqService>();
builder.Services.AddSingleton<ITaskProcessingService, TaskProcessingService>();
builder.Services.AddHostedService<TaskProcessingService>();
builder.Services.AddControllers();

var app = builder.Build();

// Настраиваем маршрутизацию
app.MapControllers();

app.Run();

// Пример appsettings.json для конфигурации RabbitMQ
/*
{
  "RabbitMq": {
    "HostName": "localhost",
    "UserName": "guest",
    "Password": "guest"
  }
}
*/
