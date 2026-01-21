using Microsoft.EntityFrameworkCore;
using API大專.Models;
using API大專.service;
using API大專.Hubs;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
var connectionString = builder.Configuration.GetConnectionString("ProxyContext");
builder.Services.AddDbContext<ProxyContext>(x => x.UseSqlServer(connectionString));

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSignalR();
builder.Services.AddScoped<CommissionService>();

// 註冊 NotificationWorker 為背景服務
builder.Services.AddHostedService<NotificationWorker>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseStaticFiles();
app.UseAuthorization();
app.MapHub<NotificationHub>("/notificationHub");
app.MapControllers();

app.Run();
