using System;
using System.Globalization;
using System.Threading;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using Bonobo.Git.Server.Configuration;
using Bonobo.Git.Server.Controllers;
using System.Runtime.Caching;
using Serilog;

namespace Bonobo.Git.Server
{
    public class MvcApplication : HttpApplication
    {
        public static ObjectCache Cache = MemoryCache.Default; 

        protected void Application_AcquireRequestState(object sender, EventArgs e)
        {
            if (HttpContext.Current.Session == null)
            {
                return;
            }

            var culture = (CultureInfo)Session["Culture"];
            if (culture == null)
            {
                culture = !String.IsNullOrEmpty(UserConfiguration.Current.DefaultLanguage)
                              ? new CultureInfo(UserConfiguration.Current.DefaultLanguage)
                              : null;

                if (culture == null)
                {
                    string langName = "en";

                    if (HttpContext.Current.Request.UserLanguages != null &&
                        HttpContext.Current.Request.UserLanguages.Length != 0 &&
                        HttpContext.Current.Request.UserLanguages[0].Length > 2)
                    {
                        langName = HttpContext.Current.Request.UserLanguages[0].Substring(0, 2);
                    }

                    culture = new CultureInfo(langName);
                    Session["Culture"] = culture;
                }
            }

            Thread.CurrentThread.CurrentUICulture = culture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.CreateSpecificCulture(culture.Name);
        }

        
        protected void Application_Error(object sender, EventArgs e)
        {
            Exception exception = Server.GetLastError();
            if (exception != null)
            {
                Response.Clear();
                HttpException httpException = exception as HttpException;

                RouteData routeData = new RouteData();
                routeData.Values.Add("controller", "Home");
                if (httpException == null)
                {
                    routeData.Values.Add("action", "Error");
                    if (exception != null)
                    {
                        Log.Error(exception, "Exception caught in Global.asax1");
                    }
                }
                else
                {
                    switch (httpException.GetHttpCode())
                    {
                        case 404:
                            routeData.Values.Add("action", "PageNotFound");
                            break;
                        case 500:
                            routeData.Values.Add("action", "ServerError");
                            Log.Error(exception, "500 Exception caught in Global.asax");
                            break;
                        default:
                            routeData.Values.Add("action", "Error");
                            Log.Error(exception, "Exception caught in Global.asax (code {Code})", httpException.GetHttpCode());
                            break;
                    }
                }
                Server.ClearError();
                Response.TrySkipIisCustomErrors = true;
                IController errorController = new HomeController();
                errorController.Execute(new RequestContext(new HttpContextWrapper(Context), routeData));
            }
        }

        

    }
}
