using System;
using Autofac;
using Bonobo.Git.Server.Data;
using Microsoft.Owin;

[assembly: OwinStartup(typeof(Bonobo.Git.Server.Startup))]

namespace Bonobo.Git.Server
{
    public class AutofacDbFactory : IDbFactory
    {
        private readonly IComponentContext _context;

        public AutofacDbFactory(IComponentContext  context)
        {
            _context = context;
        }

        public BonoboGitServerContext Create()
        {
            return _context.Resolve<BonoboGitServerContext>();
        }
    }
}
