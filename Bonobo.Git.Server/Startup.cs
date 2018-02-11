using System;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Web;
using System.Web.Configuration;
using System.Web.Helpers;
using System.Web.Hosting;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;
using Autofac;
using Autofac.Integration.Mvc;
using Bonobo.Git.Server.App_Start;
using Bonobo.Git.Server.Attributes;
using Bonobo.Git.Server.Configuration;
using Bonobo.Git.Server.Data;
using Bonobo.Git.Server.Data.Update;
using Bonobo.Git.Server.Git;
using Bonobo.Git.Server.Git.GitService;
using Bonobo.Git.Server.Git.GitService.ReceivePackHook;
using Bonobo.Git.Server.Git.GitService.ReceivePackHook.Durability;
using Bonobo.Git.Server.Git.GitService.ReceivePackHook.Hooks;
using Microsoft.Owin;
using Owin;
using Bonobo.Git.Server.Security;
using Microsoft.Owin.Extensions;
using Nancy;
using Nancy.Bootstrappers.Autofac;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Serilog;

[assembly: OwinStartup(typeof(Bonobo.Git.Server.Startup))]

namespace Bonobo.Git.Server
{
    public class Startup
    {

        public IContainer Container;
        protected ContainerBuilder Builder;

        public void Configuration(IAppBuilder app)
        {
            Application_Start();
            Container = Builder.Build();
            RecoveryDataPathSetup();
            app.Map("/api", branch =>
            {
                branch.UseNancy(options =>
                {
                    options.Bootstrapper = new ApiBootstrapper(Container);
                    options.PerformPassThrough = context => context.Response.StatusCode == HttpStatusCode.NotFound;
                });

            });
            app.UseStageMarker(PipelineStage.MapHandler);
            DependencyResolver.Current.GetService<IAuthenticationProvider>().Configure(app);
        }

        

        private void Application_Start()
        {

            var mvcAssembly = typeof(MvcApplication).Assembly;
            Builder.RegisterControllers(mvcAssembly).PropertiesAutowired(); ;
            Builder.RegisterModelBinderProvider();
            Builder.RegisterModule<AutofacWebTypesModule>();
            Builder.RegisterSource(new ViewRegistrationSource());
            Builder.RegisterFilterProvider();


            ConfigureLogging();
            Log.Information("Bonobo starting");

            AreaRegistration.RegisterAllAreas();
            BundleConfig.RegisterBundles(BundleTable.Bundles);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
            UserConfiguration.Initialize();
            RegisterDependencyResolver();
            GlobalFilters.Filters.Add((AllViewsFilter)DependencyResolver.Current.GetService<AllViewsFilter>());

            var connectionString = WebConfigurationManager.ConnectionStrings["BonoboGitServerContext"];
            if (connectionString.ProviderName.ToLowerInvariant() == "system.data.sqlite")
            {
                if (!connectionString.ConnectionString.ToLowerInvariant().Contains("binaryguid=false"))
                {
                    Log.Error("Please ensure that the sqlite connection string contains 'BinaryGUID=false;'.");
                    throw new ConfigurationErrorsException("Please ensure that the sqlite connection string contains 'BinaryGUID=false;'.");
                }
            }

            try
            {
                AntiForgeryConfig.UniqueClaimTypeIdentifier = ClaimTypes.NameIdentifier;

                new AutomaticUpdater().Run();
                new RepositorySynchronizer().Run();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Startup exception");
                throw;
            }
        }

        private static void ConfigureLogging()
        {
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.AppSettings()
                .WriteTo.RollingFile(GetLogFileNameFormat())
                .CreateLogger();
        }

        public static string GetLogFileNameFormat()
        {
            string logDirectory = ConfigurationManager.AppSettings["LogDirectory"];
            if (string.IsNullOrEmpty(logDirectory))
            {
                logDirectory = @"~\App_Data\Logs";
            }
            return Path.Combine(HostingEnvironment.MapPath(logDirectory), "log-{Date}.txt");
        }

        private void RegisterDependencyResolver()
        {
            switch (AuthenticationSettings.MembershipService.ToLowerInvariant())
            {
                case "activedirectory":
                    Builder.RegisterType<IMembershipService>().As<ADMembershipService>().PropertiesAutowired(); ;
                    Builder.RegisterType<IRoleProvider>().As<ADRoleProvider>().PropertiesAutowired(); ;
                    Builder.RegisterType<ITeamRepository>().As<ADTeamRepository>().PropertiesAutowired(); ;
                    Builder.RegisterType<IRepositoryRepository>().As<ADRepositoryRepository>().PropertiesAutowired(); ;
                    Builder.RegisterType<IRepositoryPermissionService>().As<RepositoryPermissionService>().PropertiesAutowired(); ;
                    break;
                case "internal":
                    Builder.RegisterType<IMembershipService>().As<EFMembershipService>().PropertiesAutowired(); ;
                    Builder.RegisterType<IRoleProvider>().As<EFRoleProvider>().PropertiesAutowired(); ;
                    Builder.RegisterType<ITeamRepository>().As<EFTeamRepository>().PropertiesAutowired(); ;
                    Builder.RegisterType<IRepositoryRepository>().As<EFRepositoryRepository>().PropertiesAutowired(); ;
                    Builder.RegisterType<IRepositoryPermissionService>().As<RepositoryPermissionService>().PropertiesAutowired(); ;
                    break;
                default:
                    throw new ArgumentException("Missing declaration in web.config", "MembershipService");
            }

            switch (AuthenticationSettings.AuthenticationProvider.ToLowerInvariant())
            {
                case "windows":
                    Builder.RegisterType<IAuthenticationProvider>().As<WindowsAuthenticationProvider>().PropertiesAutowired(); ;
                    break;
                case "cookies":
                    Builder.RegisterType<IAuthenticationProvider>().As<CookieAuthenticationProvider>().PropertiesAutowired(); ;
                    break;
                case "federation":
                    Builder.RegisterType<IAuthenticationProvider>().As<FederationAuthenticationProvider>().PropertiesAutowired(); ;
                    break;
                default:
                    throw new ArgumentException("Missing declaration in web.config", "AuthenticationProvider");
            }

            Builder.Register<IGitRepositoryLocator>(c=>new ConfigurationBasedRepositoryLocator(UserConfiguration.Current.Repositories)).PropertiesAutowired(); ;
            
            Builder.RegisterInstance(
                new GitServiceExecutorParams()
                {
                    GitPath = GetRootPath(ConfigurationManager.AppSettings["GitPath"]),
                    GitHomePath = GetRootPath(ConfigurationManager.AppSettings["GitHomePath"]),
                    RepositoriesDirPath = UserConfiguration.Current.Repositories,
                }).PropertiesAutowired(); ;

            Builder.RegisterType<IDatabaseResetManager>().As<DatabaseResetManager>().PropertiesAutowired(); ;

            if (AppSettings.IsPushAuditEnabled)
            {
                EnablePushAuditAnalysis(Builder);
            }

            Builder.RegisterType<IGitService>().As<GitServiceExecutor>().PropertiesAutowired(); ;

            var oldProvider = FilterProviders.Providers.Single(f => f is System.Web.Mvc.FilterAttributeFilterProvider);
            FilterProviders.Providers.Remove(oldProvider);

            var provider = new FilterAttributeFilterProvider(Builder);
            FilterProviders.Providers.Add(provider);
        }

        private static void EnablePushAuditAnalysis(ContainerBuilder builder)
        {
            var isReceivePackRecoveryProcessEnabled = !string.IsNullOrEmpty(ConfigurationManager.AppSettings["RecoveryDataPath"]);

            if (isReceivePackRecoveryProcessEnabled)
            {
                // git service execution durability registrations to enable receive-pack hook execution after failures
                builder.RegisterType<IGitService>().As<DurableGitServiceResult>().PropertiesAutowired(); ;
                builder.RegisterType<IHookReceivePack>().As<ReceivePackRecovery>().PropertiesAutowired(); ;
                builder.RegisterType<IRecoveryFilePathBuilder>().As<AutoCreateMissingRecoveryDirectories>().PropertiesAutowired(); ;
                builder.RegisterType<IRecoveryFilePathBuilder>().As<OneFolderRecoveryFilePathBuilder>().PropertiesAutowired(); ;
                builder.RegisterInstance(new NamedArguments.FailedPackWaitTimeBeforeExecution(TimeSpan.FromSeconds(5 * 60))).PropertiesAutowired(); ;

                builder.RegisterInstance(new NamedArguments.ReceivePackRecoveryDirectory(
                    Path.IsPathRooted(ConfigurationManager.AppSettings["RecoveryDataPath"]) ?
                        ConfigurationManager.AppSettings["RecoveryDataPath"] :
                        HttpContext.Current.Server.MapPath(ConfigurationManager.AppSettings["RecoveryDataPath"])));
            }

            // base git service executor
            builder.RegisterType<IGitService>().As<ReceivePackParser>().PropertiesAutowired(); ;
            builder.RegisterType<GitServiceResultParser>().As<GitServiceResultParser>().PropertiesAutowired(); ;

            // receive pack hooks
            builder.RegisterType<IHookReceivePack>().As<AuditPusherToGitNotes>().PropertiesAutowired(); ;
            builder.RegisterType<IHookReceivePack>().As<NullReceivePackHook>().PropertiesAutowired(); ;
        }

        private static string GetRootPath(string path)
        {
            return Path.IsPathRooted(path) ?
                path :
                HttpContext.Current.Server.MapPath(path);
        }
        private void RecoveryDataPathSetup()
        {
            var isReceivePackRecoveryProcessEnabled = !string.IsNullOrEmpty(ConfigurationManager.AppSettings["RecoveryDataPath"]);
            // run receive-pack recovery if possible
            if (isReceivePackRecoveryProcessEnabled)
            {
                var recoveryProcess = Container.Resolve<ReceivePackRecovery>(
                    new NamedParameter(
                        "failedPackWaitTimeBeforeExecution",
                        new NamedArguments.FailedPackWaitTimeBeforeExecution(TimeSpan.FromSeconds(0))));

                try
                {
                    recoveryProcess.RecoverAll();
                }
                catch
                {
                    // don't let a failed recovery attempt stop start-up process
                }
                finally
                {
                    if (recoveryProcess != null)
                    {
                        recoveryProcess = null;
                    }
                }
            }
        }

    }

    public class ApiBootstrapper : AutofacNancyBootstrapper
    {
        private readonly ILifetimeScope _lifetimeScope;

        public ApiBootstrapper(ILifetimeScope lifetimeScope)
        {
            _lifetimeScope = lifetimeScope;
        }

        protected override void ConfigureApplicationContainer(ILifetimeScope container)
        {
            container.Update(builder =>
            {
                builder.RegisterType<CustomJsonSerializer>().As<JsonSerializer>().SingleInstance();
            });
        }

        protected override ILifetimeScope GetApplicationContainer()
        {
            return _lifetimeScope;
        }

        private sealed class CustomJsonSerializer : JsonSerializer
        {
            public CustomJsonSerializer()
            {
                ContractResolver = new DefaultContractResolver();
                Formatting = Formatting.Indented;
            }
        }
    }
}
