using System;
using Autofac;
using ILogger = Vem.Common.Logging.Interfaces.ILogger;

namespace Bonobo.Git.Server
{
    public class VemLogFactory : Vem.Common.Logging.Interfaces.ILogFactory
    {
        private readonly IComponentContext _container;

        public VemLogFactory(IComponentContext container)
        {
            _container = container;
        }


        public ILogger CreateSeriLogger(Serilog.ILogger logger)
        {
            return _container.Resolve<ILogger>();
        }

        public void Release(IDisposable instance)
        {
            ;
        }
    }
}