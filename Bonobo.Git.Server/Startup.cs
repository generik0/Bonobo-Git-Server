using System;
using System.Configuration;
using System.Data.Entity;
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
using Vem.Common.Logging;
using Vem.Common.Logging.Interfaces;

[assembly: OwinStartup(typeof(Bonobo.Git.Server.Startup))]

namespace Bonobo.Git.Server
{
    public class Startup
    {

        public IContainer Container;
        protected ContainerBuilder Builder = new ContainerBuilder();

        public void Configuration(IAppBuilder app)
        {
            Application_Start();
            Container = Builder.Build();
            DependencyResolver.SetResolver(new AutofacDependencyResolver(Container));
            AreaRegistration.RegisterAllAreas();
            BundleConfig.RegisterBundles(BundleTable.Bundles);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
            UserConfiguration.Initialize();



            GlobalFilters.Filters.Add(Container.Resolve<AllViewsFilter>());

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
            app.UseAutofacMvc();
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

            Builder.RegisterAssemblyTypes(mvcAssembly)
                .Where(type => typeof(Attribute).IsAssignableFrom(type))
                .AsSelf()
                .SingleInstance();
            
            ConfigureLogging();
            Log.Information("Bonobo starting");
            RegisterDependencyResolver();
        }

        private static void ConfigureLogging()
        {
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.AppSettings()
                .CreateLogger();
        }

       private void RegisterDependencyResolver()
        {
            switch (AuthenticationSettings.MembershipService.ToLowerInvariant())
            {
                case "activedirectory":
                    Builder.RegisterType<ADMembershipService>().As<IMembershipService>().PropertiesAutowired(); ;
                    Builder.RegisterType<ADRoleProvider>().As<IRoleProvider>().PropertiesAutowired(); ;
                    Builder.RegisterType<ADTeamRepository>().As<ITeamRepository>().PropertiesAutowired(); ;
                    Builder.RegisterType<ADRepositoryRepository>().As<IRepositoryRepository>().PropertiesAutowired(); ;
                    Builder.RegisterType<RepositoryPermissionService>().As<IRepositoryPermissionService>().PropertiesAutowired(); ;
                    break;
                case "internal":
                    Builder.RegisterType<EFMembershipService>().As<IMembershipService>().PropertiesAutowired(); ;
                    Builder.RegisterType<EFRoleProvider>().As<IRoleProvider>().PropertiesAutowired(); ;
                    Builder.RegisterType<EFTeamRepository>().As<ITeamRepository>().PropertiesAutowired(); ;
                    Builder.RegisterType<EFRepositoryRepository>().As<IRepositoryRepository>().PropertiesAutowired(); ;
                    Builder.RegisterType<RepositoryPermissionService>().As<IRepositoryPermissionService>().PropertiesAutowired();
                    
                    break;
                default:
                    throw new ArgumentException("Missing declaration in web.config", "MembershipService");
            }

            Builder.RegisterType<BonoboGitServerContext>().AsSelf().InstancePerDependency();
            Builder.Register(c => new AutofacDbFactory(c.Resolve<IComponentContext>())).As<IDbFactory>().SingleInstance();

            switch (AuthenticationSettings.AuthenticationProvider.ToLowerInvariant())
            {
                case "windows":
                    Builder.RegisterType<WindowsAuthenticationProvider>().As<IAuthenticationProvider>().PropertiesAutowired(); ;
                    break;
                case "cookies":
                    Builder.RegisterType<CookieAuthenticationProvider>().As<IAuthenticationProvider>().PropertiesAutowired(); ;
                    break;
                case "federation":
                    Builder.RegisterType<FederationAuthenticationProvider>().As<IAuthenticationProvider>().PropertiesAutowired(); ;
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
                    RepositoriesDirPath = UserConfiguration.Current?.Repositories ?? ConfigurationManager.AppSettings["DefaultRepositoriesDirectory"],
                }).PropertiesAutowired(); ;

            Builder.RegisterType<DatabaseResetManager>().As<IDatabaseResetManager>().PropertiesAutowired(); ;

            if (AppSettings.IsPushAuditEnabled)
            {
                EnablePushAuditAnalysis(Builder);
            }

            Builder.RegisterType<GitServiceExecutor>().As<IGitService>().PropertiesAutowired(); ;
            Builder.Register(c => new VemLogFactory(c.Resolve<IComponentContext>())).As<ILogFactory>().SingleInstance();
            Builder.Register(c => new SerilogLogger(Log.Logger, c.Resolve<ILogFactory>(), new SerilogLogAggregator())).As<Vem.Common.Logging.Interfaces.ILogger>().SingleInstance();

        }
        

        private static void EnablePushAuditAnalysis(ContainerBuilder builder)
        {
            var isReceivePackRecoveryProcessEnabled = !string.IsNullOrEmpty(ConfigurationManager.AppSettings["RecoveryDataPath"]);

            if (isReceivePackRecoveryProcessEnabled)
            {
                // git service execution durability registrations to enable receive-pack hook execution after failures
                builder.RegisterType<DurableGitServiceResult> ().As<IGitService>().PropertiesAutowired(); ;
                builder.RegisterType<ReceivePackRecovery>().As<IHookReceivePack>().PropertiesAutowired(); ;
                builder.RegisterType<AutoCreateMissingRecoveryDirectories>().As<IRecoveryFilePathBuilder>().PropertiesAutowired(); ;
                builder.RegisterType<OneFolderRecoveryFilePathBuilder>().As<IRecoveryFilePathBuilder>().PropertiesAutowired(); ;
                builder.RegisterInstance(new NamedArguments.FailedPackWaitTimeBeforeExecution(TimeSpan.FromSeconds(5 * 60))).PropertiesAutowired(); ;

                builder.RegisterInstance(new NamedArguments.ReceivePackRecoveryDirectory(
                    Path.IsPathRooted(ConfigurationManager.AppSettings["RecoveryDataPath"]) ?
                        ConfigurationManager.AppSettings["RecoveryDataPath"] :
                        HttpContext.Current.Server.MapPath(ConfigurationManager.AppSettings["RecoveryDataPath"])));
            }

            // base git service executor
            builder.RegisterType<ReceivePackParser>().As<IGitService>().PropertiesAutowired(); ;
            builder.RegisterType<GitServiceResultParser>().As<GitServiceResultParser>().PropertiesAutowired(); ;

            // receive pack hooks
            builder.RegisterType<AuditPusherToGitNotes>().As<IHookReceivePack>().PropertiesAutowired(); ;
            builder.RegisterType<NullReceivePackHook>().As<IHookReceivePack>().PropertiesAutowired(); ;
        }

        private static string GetRootPath(string path)
        {
            return Path.IsPathRooted(path) ?
                path :
                HttpContext.Current.Server.MapPath(path);
        }
        private void RecoveryDataPathSetup()
        {
            if(!AppSettings.IsPushAuditEnabled) return;

            var isReceivePackRecoveryProcessEnabled = !string.IsNullOrEmpty(ConfigurationManager.AppSettings["RecoveryDataPath"]);
            // run receive-pack recovery if possible
            if (isReceivePackRecoveryProcessEnabled)
            {
                ReceivePackRecovery recoveryProcess = Container.Resolve<IHookReceivePack>(
                    new NamedParameter(
                        "failedPackWaitTimeBeforeExecution",
                        new NamedArguments.FailedPackWaitTimeBeforeExecution(TimeSpan.FromSeconds(0)))) as ReceivePackRecovery;

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
