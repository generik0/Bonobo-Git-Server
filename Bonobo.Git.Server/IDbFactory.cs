using Bonobo.Git.Server.Data;
using Microsoft.Owin;

[assembly: OwinStartup(typeof(Bonobo.Git.Server.Startup))]

namespace Bonobo.Git.Server
{
    public interface IDbFactory
    {
        BonoboGitServerContext Create();
    }
}
