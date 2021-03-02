﻿//*********************************************************************
//CAD+ Toolset
//Copyright(C) 2020 Xarial Pty Limited
//Product URL: https://cadplus.xarial.com
//License: https://cadplus.xarial.com/license/
//*********************************************************************

using System;
using System.Linq;
using System.Runtime.InteropServices;
using Xarial.XCad.SolidWorks;
using Xarial.XCad.Base.Attributes;
using System.ComponentModel;
using Xarial.XToolkit.Wpf.Dialogs;
using Xarial.CadPlus.AddIn.Base;
using Xarial.XCad;
using Xarial.XCad.Base;
using Xarial.CadPlus.Common.Services;
using Xarial.XCad.UI.PropertyPage;
using System.Reflection;
using Xarial.XToolkit.Reflection;
using Xarial.CadPlus.Plus;
using Autofac;
using Xarial.CadPlus.Common;
using Xarial.CadPlus.Init;
using Xarial.CadPlus.Plus.Applications;
using Xarial.CadPlus.Plus.Shared;
using Xarial.CadPlus.Plus.Services;
using Xarial.CadPlus.Plus.Extensions;

namespace Xarial.CadPlus.AddIn.Sw
{
    [ComVisible(true), Guid("AC45BDF0-66CB-4B08-8127-06C1F0C9452F")]
    [Title("CAD+ Toolset")]
    [Description("The toolset of utilities to complement SOLIDWORKS functionality")]
    public class CadPlusSwAddIn : SwAddInEx
    {
        private class LocalAppConfigBindingRedirectReferenceResolver : AppConfigBindingRedirectReferenceResolver
        {
            protected override Assembly[] GetRequestingAssemblies(Assembly requestingAssembly)
            {
                if (requestingAssembly != null)
                {
                    return new Assembly[] { requestingAssembly };
                }
                else
                {
                    return new Assembly[] 
                    {
                        typeof(CadPlusSwAddIn).Assembly,
                        typeof(CadPlusCommands_e).Assembly 
                    };
                }
            }   
        }

        static CadPlusSwAddIn() 
        {
            AppDomain.CurrentDomain.ResolveBindingRedirects(new LocalAppConfigBindingRedirectReferenceResolver());
        }

        private readonly AddInHost m_Host;

        public CadPlusSwAddIn()
        {
            m_Host = new AddInHost(new SwAddInApplication(this), 
                new Initiator());
            m_Host.ConfigureServices += OnConfigureModuleServices;
        }
        
        private void OnConfigureModuleServices(IContainerBuilder builder)
        {
            var svc = ((ContainerBuilderWrapper)builder).Builder;

            svc.RegisterType<SwPropertyPageCreator<SwGeneralPropertyManagerPageHandler>>()
                .As<IPropertyPageCreator>()
                .WithParameter(new TypedParameter(typeof(ISwAddInEx), this));

            builder.RegisterAdapter<IXApplication, ISwApplication>(a => (ISwApplication)a);
            builder.Register(x => x.GetService<IMacroRunnerExService>(CadApplicationIds.SolidWorks));
            builder.Register(x => x.GetService<ICadEntityDescriptor>(CadApplicationIds.SolidWorks));
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        }
    }
}
