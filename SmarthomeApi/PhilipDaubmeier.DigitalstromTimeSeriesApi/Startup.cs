﻿using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PhilipDaubmeier.DigitalstromHost.DependencyInjection;
using PhilipDaubmeier.DigitalstromTimeSeriesApi.Database;
using System;

namespace PhilipDaubmeier.DigitalstromTimeSeriesApi
{
    public class Startup
    {
        public Startup(IConfiguration configuration, IHostingEnvironment environment)
        {
            Configuration = configuration;
            Environment = environment;
        }

        public IConfiguration Configuration { get; }
        public IHostingEnvironment Environment { get; }
        
        public IServiceProvider ConfigureServices(IServiceCollection services)
        {
            services.AddMvc();

            var connectionString = Configuration.GetConnectionString("DigitalstromTimeSeriesDB");
            return services.AddOptions()
                .AddLogging(config =>
                {
                    config.ClearProviders();
                    config.AddConfiguration(Configuration.GetSection("Logging"));

                    if (Environment.IsDevelopment())
                    {
                        config.AddConsole();
                        config.AddDebug();
                        config.AddEventSourceLogger();
                    }
                })
                .AddDbContext<DigitalstromTimeSeriesDbContext>(options =>
                {
                    options.UseSqlServer(connectionString);
                })
                .AddDigitalstromHost<DigitalstromTimeSeriesDbContext>(options =>
                    {
                        options.UseSqlServer(connectionString);
                    }, 
                    Configuration.GetSection("DigitalstromConfig"), 
                    Configuration.GetSection("TokenStoreConfig")
                )
                .BuildServiceProvider();
        }
        
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, IServiceProvider serviceProvider)
        {
            if (env.IsDevelopment())
                app.UseDeveloperExceptionPage();

            app.UseMvc();
            
            serviceProvider.GetRequiredService<DigitalstromTimeSeriesDbContext>().Database.Migrate();
        }
    }
}