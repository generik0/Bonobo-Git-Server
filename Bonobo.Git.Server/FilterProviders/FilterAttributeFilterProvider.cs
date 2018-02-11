using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Autofac;

public class FilterAttributeFilterProvider : System.Web.Mvc.FilterAttributeFilterProvider
{
    private readonly ContainerBuilder _builder;

    public FilterAttributeFilterProvider(ContainerBuilder builder)
    {
        _builder = builder;
    }

    protected override IEnumerable<FilterAttribute> GetControllerAttributes(ControllerContext controllerContext, ActionDescriptor actionDescriptor)
    {
        var attributes = base.GetControllerAttributes(controllerContext, actionDescriptor).ToList();
        foreach (var attribute in attributes)
        {
            //_builder.BuildUp(attribute.GetType(), attribute);
            _builder.RegisterType(attribute.GetType()).OnActivated(e => e.Context.InjectUnsetProperties(e.Instance));
        }

        return attributes;
    }

    protected override IEnumerable<FilterAttribute> GetActionAttributes(ControllerContext controllerContext, ActionDescriptor actionDescriptor)
    {
        var attributes = base.GetActionAttributes(controllerContext, actionDescriptor).ToList();
        foreach (var attribute in attributes)
        {
            //_builder.BuildUp(attribute.GetType(), attribute);
            _builder.RegisterType(attribute.GetType()).OnActivated(e => e.Context.InjectUnsetProperties(e.Instance));
        }

        return attributes;
    }
}