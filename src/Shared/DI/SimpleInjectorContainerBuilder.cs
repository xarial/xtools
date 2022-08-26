﻿//*********************************************************************
//CAD+ Toolset
//Copyright(C) 2022 Xarial Pty Limited
//Product URL: https://cadplus.xarial.com
//License: https://cadplus.xarial.com/license/
//*********************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xarial.XToolkit.Reflection;
using Xarial.CadPlus.Plus.DI;
using Xarial.XCad;

namespace Xarial.CadPlus.Plus.Shared.DI
{
    internal static class SimpleInjectorContainerExtension
    {
        internal static void RegisterInitializer(this SimpleInjector.Container cont, Type svcType, Delegate initializer)
            => Lambda.InvokeGenericMethod(() => cont.RegisterInitializer<object>((Action<object>)initializer), svcType);

        internal static void Append(this SimpleInjector.ContainerCollectionRegistrator contCollReg,
            Type svcType, Delegate instanceCreator, SimpleInjector.Lifestyle lifestyle)
            => Lambda.InvokeGenericMethod(() => contCollReg.Append<object>((Func<object>)instanceCreator, lifestyle), svcType);
    }

    public class SimpleInjectorContainerBuilder : IContainerBuilder
    {
        private class RegistrationInfo 
        {
            internal IRegistration Registration { get; }
            internal RegistrationConflictResolveStrategy_e ConflictResolve { get; }

            internal RegistrationInfo(IRegistration registration, RegistrationConflictResolveStrategy_e conflictResolve)
            {
                Registration = registration;
                ConflictResolve = conflictResolve;
            }
        }

        public event Action<IContainerBuilder, IServiceProvider> ContainerCreated;

        protected SimpleInjectorServiceProvider m_Provider;

        private readonly List<RegistrationInfo> m_Registrations;

        private readonly SimpleInjector.Container m_Container;

        public SimpleInjectorContainerBuilder()
        {
            m_Registrations = new List<RegistrationInfo>();
            m_Container = new SimpleInjector.Container();
            m_Container.Options.EnableAutoVerification = false;
        }

        public IServiceProvider Build()
        {
            ValidateStateIsBuilding();

            //NOTE: creating the service provider so it can be passed to parameters selector
            m_Provider = new SimpleInjectorServiceProvider(m_Container);

            var registrations = ResolveRegistrations();

            foreach (var reg in registrations)
            {
                var lifestyle = GetLifestyle(reg.Lifetime);

                if (reg.Factory == null)
                {
                    if (reg.Parameters == null)
                    {
                        if (!reg.IsCollectionItem)
                        {
                            m_Container.Register(reg.ServiceType, reg.ImplementationType, lifestyle);
                        }
                        else
                        {
                            m_Container.Collection.Append(reg.ServiceType, reg.ImplementationType, lifestyle);
                        }
                    }
                    else
                    {
                        var instanceCreator = CreateInstanceFactoryWithParameters(m_Container, m_Provider, reg.ImplementationType, reg.Parameters);

                        if (!reg.IsCollectionItem)
                        {
                            m_Container.Register(reg.ServiceType, instanceCreator, lifestyle);
                        }
                        else
                        {
                            m_Container.Collection.Append(reg.ServiceType, instanceCreator, lifestyle);
                        }
                    }
                }
                else if (reg.Factory != null)
                {
                    if (!reg.IsCollectionItem)
                    {
                        m_Container.Register(reg.ServiceType, (Func<object>)reg.Factory, lifestyle);
                    }
                    else
                    {
                        m_Container.Collection.Append(reg.ServiceType, reg.Factory, lifestyle);
                    }
                }

                if (reg.Initializer != null)
                {
                    if (reg.Factory == null)
                    {
                        m_Container.RegisterInitializer(reg.ImplementationType, reg.Initializer);
                    }
                    else
                    {
                        throw new NotSupportedException("Initializer is not supported when factory is specified");
                    }
                }
            }

            ContainerCreated?.Invoke(this, m_Provider);

            return m_Provider;
        }

        private IReadOnlyList<IRegistration> ResolveRegistrations() 
        {
            var registrations = new List<IRegistration>();

            foreach (var regInfo in m_Registrations) 
            {
                var existingRegInd = regInfo.Registration.IsCollectionItem ? -1 : registrations.FindIndex(r => r.ServiceType == regInfo.Registration.ServiceType);

                if (existingRegInd == -1)
                {
                    registrations.Add(regInfo.Registration);
                }
                else
                {
                    switch (regInfo.ConflictResolve)
                    {
                        case RegistrationConflictResolveStrategy_e.Replace:
                            registrations[existingRegInd] = regInfo.Registration;
                            break;

                        case RegistrationConflictResolveStrategy_e.KeepOriginal:
                            //Do nothing
                            break;

                        case RegistrationConflictResolveStrategy_e.ThrownError:
                            throw new Exception($"'{regInfo.Registration.ServiceType.FullName}' is already registered");
                    }
                }
            }

            return registrations;
        }

        private static SimpleInjector.Lifestyle GetLifestyle(LifetimeScope_e scope)
        {
            SimpleInjector.Lifestyle lifestyle;

            switch (scope)
            {
                case LifetimeScope_e.Singleton:
                    lifestyle = SimpleInjector.Lifestyle.Singleton;
                    break;

                case LifetimeScope_e.Transient:
                    lifestyle = SimpleInjector.Lifestyle.Transient;
                    break;

                default:
                    throw new NotSupportedException();
            }

            return lifestyle;
        }

        public void Register(IRegistration registration, RegistrationConflictResolveStrategy_e conflictResolveStrategy)
        {
            ValidateStateIsBuilding();

            m_Registrations.Add(new RegistrationInfo(registration, conflictResolveStrategy));
        }

        public void RegisterAdapter(Type fromSvcType, Type toSvcType, Func<object, object> adapter, LifetimeScope_e scope)
        {
            ValidateStateIsBuilding();

            var lifestyle = GetLifestyle(scope);

            m_Container.Register(toSvcType, () => adapter.Invoke(m_Container.GetInstance(fromSvcType)), lifestyle);
        }

        public void RegisterInstance(Type svcType, object inst)
        {
            ValidateStateIsBuilding();

            m_Container.RegisterInstance(svcType, inst);
        }

        public void RegisterDecorator(Type svcType, Type decorType, LifetimeScope_e scope)
        {
            ValidateStateIsBuilding();

            var lifestyle = GetLifestyle(scope);

            m_Container.RegisterDecorator(svcType, decorType, lifestyle);
        }

        private void ValidateStateIsBuilding()
        {
            if (m_Provider != null)
            {
                throw new Exception("Container is already build");
            }
        }

        private Func<object> CreateInstanceFactoryWithParameters(SimpleInjector.Container container, IServiceProvider svcProvider, 
            Type impType, IParameter[] paramSelectors)
            => new Func<object>(() =>
            {
                var targetConstructor = FindConstructor(container, svcProvider, impType, paramSelectors, out var targetParameters);
                return targetConstructor.Invoke(targetParameters.Select(p => p.Invoke()).ToArray());
            });

        private ConstructorInfo FindConstructor(SimpleInjector.Container container, IServiceProvider svcProvider,
            Type impType, IParameter[] paramSelectors, out Func<object>[] targetParameters) 
        {
            var constructors = impType.GetConstructors();

            ConstructorInfo targetConstructor = null;
            targetParameters = null;

            foreach (var constructor in constructors)
            {
                if (IsConstructorMatch(constructor, container, svcProvider, paramSelectors, out Func<object>[] parameters))
                {
                    if (targetConstructor == null)
                    {
                        targetConstructor = constructor;
                        targetParameters = parameters;
                    }
                    else
                    {
                        throw new Exception($"Multiple constructors were matched for the type '{impType.FullName}' based on the input parameters");
                    }
                }
            }

            if (targetConstructor == null)
            {
                throw new Exception($"Failed to find the constructor for type '{impType.FullName}' matching the parameters");
            }

            return targetConstructor;
        }

        private bool IsConstructorMatch(ConstructorInfo constructor, SimpleInjector.Container container, IServiceProvider svcProvider,
            IParameter[] paramSelectors, out Func<object>[] paramsProviders)
        {
            var constructorParameters = constructor.GetParameters() ?? new ParameterInfo[0];

            paramsProviders = new Func<object>[constructorParameters.Length];

            for (int i = 0; i < constructorParameters.Length; i++)
            {
                var parameter = constructorParameters[i];

                var scopedParamSelectors = paramSelectors.Where(s => s.Matches(parameter, i)).ToArray();

                if (scopedParamSelectors.Length > 1)
                {
                    return false;
                }
                else if (scopedParamSelectors.Length == 1)
                {
                    paramsProviders[i] = new Func<object>(() => scopedParamSelectors.First().ProvideValue(parameter, svcProvider));
                }
                else
                {
                    var instProducer = container.GetRegistration(parameter.ParameterType, false);
                    
                    if (instProducer != null)
                    {
                        paramsProviders[i] = new Func<object>(() => instProducer.GetInstance());
                    }
                    else
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }

    public class SimpleInjectorServiceCollectionContainerBuilder : SimpleInjectorContainerBuilder, IXServiceCollection
    {
        public void Add(Type svcType, Func<object> svcFactory, ServiceLifetimeScope_e lifetime = ServiceLifetimeScope_e.Singleton, bool replace = true)
        {
            var reg = this.Register(svcType, svcFactory,
                replace ? RegistrationConflictResolveStrategy_e.Replace : RegistrationConflictResolveStrategy_e.KeepOriginal);

            switch (lifetime)
            {
                case ServiceLifetimeScope_e.Singleton:
                    reg.Lifetime = LifetimeScope_e.Singleton;
                    break;

                case ServiceLifetimeScope_e.Transient:
                    reg.Lifetime = LifetimeScope_e.Transient;
                    break;

                default:
                    throw new NotSupportedException();
            }
        }

        //NOTE: implement this if macro features are added to CAD+
        public IXServiceCollection Clone() => throw new NotSupportedException();

        public IServiceProvider CreateProvider()
        {
            if (m_Provider != null)
            {
                return m_Provider;
            }
            else 
            {
                throw new Exception("Provider is not built");
            }
        }
    }
}
