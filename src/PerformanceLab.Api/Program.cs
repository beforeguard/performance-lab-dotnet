using PerformanceLab.Api.Middleware;
using PerformanceLab.Application.Users;
using PerformanceLab.Application.Users.Abstractions;
using PerformanceLab.Infrastructure.Users;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddScoped<UserService>();
builder.Services.AddSingleton<IUserRepository, UserRepository>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddOutputCache(options =>
{
    options.AddPolicy("UsersCachePolicy", builder => 
        builder.Expire(TimeSpan.FromSeconds(60))
               .SetVaryByQuery("*")
               .Tag("users"));
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCacheLogging();

app.UseOutputCache();

app.MapControllers();

app.Run();