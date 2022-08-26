// Copyright (c) Brock Allen & Dominick Baier. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.


using IdentityModel;
using IdentityServer4;
using IdentityServer4.EntityFramework.DbContexts;
using IdentityServer4.EntityFramework.Mappers;
using IdentityServer4.Test;
//using IdentityServerAspNetIdentity.Data;
//using IdentityServerAspNetIdentity.Models;
using IdentityServerAspNetIdentity1.Data;
using IdentityServerAspNetIdentity1.Models;
using IdentityServerAspNetIdentity1;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;

namespace IdentityServerAspNetIdentity
{
    public class Startup
    {
        public IWebHostEnvironment Environment { get; }
        public IConfiguration Configuration { get; }

        public Startup(IWebHostEnvironment environment, IConfiguration configuration)
        {
            Environment = environment;
            Configuration = configuration;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            var connectionString = Configuration.GetConnectionString("DefaultConnection");
            var migrationsAssembly = typeof(Startup).GetTypeInfo().Assembly.GetName().Name;

            services.AddControllersWithViews().AddJsonOptions(config => config.JsonSerializerOptions.PropertyNameCaseInsensitive = true);

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlServer(connectionString));

            services.AddIdentity<ApplicationUser, IdentityRole>()
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddDefaultTokenProviders();

            var builder = services.AddIdentityServer(options =>
            {
                options.Events.RaiseErrorEvents = true;
                options.Events.RaiseInformationEvents = true;
                options.Events.RaiseFailureEvents = true;
                options.Events.RaiseSuccessEvents = true;

                // see https://identityserver4.readthedocs.io/en/latest/topics/resources.html
                options.EmitStaticAudienceClaim = true;
            })
             .AddConfigurationStore(options =>
             {
                 options.ConfigureDbContext = b => b.UseSqlServer(connectionString,
                     sql => sql.MigrationsAssembly(migrationsAssembly));
             })
                .AddOperationalStore(options =>
                {
                    options.ConfigureDbContext = b => b.UseSqlServer(connectionString,
                    sql => sql.MigrationsAssembly(migrationsAssembly));
                })
                .AddAspNetIdentity<ApplicationUser>();

            services.AddCors();

            //services.AddTransient<EmailSender, EmailSender>();
            //services.Configure<AuthMessageSenderOptions>(Configuration);

            // not recommended for production - you need to store your key material somewhere secure
            builder.AddDeveloperSigningCredential();

            services.AddAuthentication()
                .AddOpenIdConnect("aad", "Azure AD", options =>
                {
                    options.SignInScheme = IdentityServerConstants.ExternalCookieAuthenticationScheme;
                    options.SignOutScheme = IdentityServerConstants.SignoutScheme;

                    options.Authority = "https://login.windows.net/df01f645-4c6a-4480-9f2b-ad411305e8e7";
                    options.ClientId = "3a56eb48-02e7-4864-98c8-cf5eeb7536f0";
                    options.ResponseType = OpenIdConnectResponseType.IdToken;
                    options.CallbackPath = "/signin-oidc";
                    options.SignedOutCallbackPath = "/signout-callback-oidc";
                    options.RemoteSignOutPath = "/signout-callback-oidc";
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        NameClaimType = "name",
                        RoleClaimType = "role"
                    };
                });
            //.AddOpenIdConnect("aad1", "Azure AD1", options =>
            //{
            //    options.SignInScheme = IdentityServerConstants.ExternalCookieAuthenticationScheme;
            //    options.SignOutScheme = IdentityServerConstants.SignoutScheme;

            //    options.Authority = "https://login.windows.net/df01f645-4c6a-4480-9f2b-ad411305e8e7";
            //    options.ClientId = "3b2f1548-10ea-427c-9c4d-643ca16a4c6f";
            //    options.ResponseType = OpenIdConnectResponseType.IdToken;
            //    options.CallbackPath = "/signin-oidc";
            //    options.SignedOutCallbackPath = "/signout-callback-oidc";
            //    options.RemoteSignOutPath = "/signout-callback-oidc";
            //    options.TokenValidationParameters = new TokenValidationParameters
            //    {
            //        NameClaimType = "name",
            //        RoleClaimType = "role"
            //    };
            //});
        }

        public void Configure(IApplicationBuilder app)
        {
            var connectionString = Configuration.GetConnectionString("DefaultConnection");
            this.InitializeDatabase(app);
            if (Environment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseDatabaseErrorPage();
            }

            app.UseCors(builder =>
            {
                builder.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
            });

            app.UseStaticFiles();

            app.UseRouting();
            app.UseIdentityServer();
            app.UseAuthorization();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapDefaultControllerRoute();
            });
        }

        private void InitializeDatabase(IApplicationBuilder app)
        {
            using (var serviceScope = app.ApplicationServices.GetService<IServiceScopeFactory>().CreateScope())
            {
                serviceScope.ServiceProvider.GetRequiredService<PersistedGrantDbContext>().Database.Migrate();

                var userContext = serviceScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                userContext.Database.Migrate();

                var context = serviceScope.ServiceProvider.GetRequiredService<ConfigurationDbContext>();
                context.Database.Migrate();
                if (!context.Clients.Any())
                {
                    foreach (var client in Config.Clients)
                    {
                        context.Clients.Add(client.ToEntity());
                    }
                    context.SaveChanges();
                }

                if (!context.IdentityResources.Any())
                {
                    foreach (var resource in Config.IdentityResources)
                    {
                        context.IdentityResources.Add(resource.ToEntity());
                    }
                    context.SaveChanges();
                }

                if (!context.ApiScopes.Any())
                {
                    foreach (var resource in Config.ApiScopes)
                    {
                        context.ApiScopes.Add(resource.ToEntity());
                    }
                    context.SaveChanges();
                }

                var userMgr = serviceScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var alice = userMgr.FindByNameAsync("alice").Result;
                if (alice == null)
                {
                    alice = new ApplicationUser
                    {
                        UserName = "alice",
                        Email = "AliceSmith@email.com",
                        EmailConfirmed = true,
                    };
                    var result = userMgr.CreateAsync(alice, "Pass123$").Result;
                    if (!result.Succeeded)
                    {
                        throw new Exception(result.Errors.First().Description);
                    }

                    //result = userMgr.AddClaimsAsync(alice, new Claim[]{
                    //        new Claim(JwtClaimTypes.Name, "Alice Smith"),
                    //        new Claim(JwtClaimTypes.GivenName, "Alice"),
                    //        new Claim(JwtClaimTypes.FamilyName, "Smith"),
                    //        new Claim(JwtClaimTypes.WebSite, "http://alice.com"),
                    //    }).Result;
                    if (!result.Succeeded)
                    {
                        throw new Exception(result.Errors.First().Description);
                    }
                    Log.Debug("alice created");
                }
                else
                {
                    Log.Debug("alice already exists");
                }

                var bob = userMgr.FindByNameAsync("bob").Result;
                if (bob == null)
                {
                    bob = new ApplicationUser
                    {
                        UserName = "bob",
                        Email = "BobSmith@email.com",
                        EmailConfirmed = true
                    };
                    var result = userMgr.CreateAsync(bob, "Pass123$").Result;
                    if (!result.Succeeded)
                    {
                        throw new Exception(result.Errors.First().Description);
                    }

                    //result = userMgr.AddClaimsAsync(bob, new Claim[]{
                    //        new Claim(JwtClaimTypes.Name, "Bob Smith"),
                    //        new Claim(JwtClaimTypes.GivenName, "Bob"),
                    //        new Claim(JwtClaimTypes.FamilyName, "Smith"),
                    //        new Claim(JwtClaimTypes.WebSite, "http://bob.com"),
                    //        new Claim("location", "somewhere")
                    //    }).Result;
                    if (!result.Succeeded)
                    {
                        throw new Exception(result.Errors.First().Description);
                    }
                    Log.Debug("bob created");
                }
                else
                {
                    Log.Debug("bob already exists");
                }
            }
        }
    }
}