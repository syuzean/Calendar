using Calendar.Components;
using Calendar.Data;
using Calendar.Models;
using Calendar.Services;
using Calendar.Services.Email;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Calendar
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();
            builder.Services.AddCascadingAuthenticationState();
            builder.Services.AddDbContextFactory<CalendarDbContext>(options =>
                options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
            builder.Services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<CalendarDbContext>>().CreateDbContext());
            builder.Services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
            builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options =>
                {
                    options.LoginPath = "/login";
                    options.Cookie.Name = "Luma.Auth";
                    options.Cookie.HttpOnly = true;
                    options.Cookie.SameSite = SameSiteMode.Lax;
                    options.ExpireTimeSpan = TimeSpan.FromDays(30);
                    options.SlidingExpiration = true;
                });
            builder.Services.AddAuthorization();
            builder.Services.Configure<SmtpOptions>(options =>
            {
                builder.Configuration.GetSection(SmtpOptions.SectionName).Bind(options);
                options.Password = Environment.GetEnvironmentVariable(SmtpOptions.PasswordEnvironmentVariable) ?? string.Empty;
            });
            builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
            builder.Services.AddSingleton<IEventShareNotifier, EventShareNotifier>();
            builder.Services.AddScoped<CalendarStore>();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();

            app.UseStaticFiles();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseAntiforgery();

            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CalendarDbContext>();
                db.Database.Migrate();
            }

            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();
            app.MapAccountEndpoints();

            app.Run();
        }
    }
}
