using Nancy;

namespace Bonobo.Git.Server.Modules
{
    public class SecurityModule : NancyModule
    {
        public SecurityModule()
        {
            Post["Security/login"] = _ => Test();
        }

        private static Response Test()
        {

            return HttpStatusCode.Continue;
        }
    }
}