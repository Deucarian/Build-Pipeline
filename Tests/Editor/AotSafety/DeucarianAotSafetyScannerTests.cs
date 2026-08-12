using System;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NUnit.Framework;

namespace Deucarian.BuildPipeline.Tests
{
    public sealed class DeucarianAotSafetyScannerTests
    {
        [Test]
        public void Scan_FindsRuntimeActivatorConstruction()
        {
            using (CecilAssemblyFixture fixture =
                   new CecilAssemblyFixture(
                       "Game.Runtime",
                       referencesNewtonsoft: false))
            {
                TypeDefinition type = fixture.AddType("Game", "Factory");
                MethodInfo createInstance = typeof(Activator).GetMethod(
                    "CreateInstance",
                    new[] { typeof(Type) });
                AddCall(fixture.Module, type, createInstance);
                string path = fixture.Write();

                DeucarianAotSafetyReport report = Scan(fixture, path);

                Assert.That(report.passed, Is.False);
                Assert.That(
                    report.findings.Any(finding =>
                        finding.category == "DynamicConstruction"
                        && finding.calledApi ==
                        "System.Activator::CreateInstance"),
                    Is.True);
            }
        }

        [Test]
        public void Scan_FindsReflectionBasedNewtonsoftMapping()
        {
            using (CecilAssemblyFixture fixture =
                   new CecilAssemblyFixture("Game.Runtime"))
            {
                TypeDefinition type = fixture.AddType("Game", "Parser");
                MethodReference deserialize = new MethodReference(
                    "DeserializeObject",
                    fixture.Module.TypeSystem.Object,
                    fixture.NewtonsoftType("JsonConvert"))
                {
                    HasThis = false
                };
                deserialize.Parameters.Add(
                    new ParameterDefinition(fixture.Module.TypeSystem.String));
                AddCall(fixture.Module, type, deserialize);
                string path = fixture.Write();

                DeucarianAotSafetyReport report = Scan(fixture, path);

                Assert.That(report.passed, Is.False);
                Assert.That(
                    report.findings.Any(finding =>
                        finding.category == "ReflectionBasedSerialization"),
                    Is.True);
            }
        }

        [Test]
        public void Scan_FindsReflectionEmitCalls()
        {
            using (CecilAssemblyFixture fixture =
                   new CecilAssemblyFixture(
                       "Game.Runtime",
                       referencesNewtonsoft: false))
            {
                TypeDefinition type = fixture.AddType("Game", "Compiler");
                TypeReference emitType = new TypeReference(
                    "System.Reflection.Emit",
                    "DynamicMethod",
                    fixture.Module,
                    fixture.Module.TypeSystem.CoreLibrary);
                MethodReference create = new MethodReference(
                    "Create",
                    fixture.Module.TypeSystem.Object,
                    emitType)
                {
                    HasThis = false
                };
                AddCall(fixture.Module, type, create);
                string path = fixture.Write();

                DeucarianAotSafetyReport report = Scan(fixture, path);

                Assert.That(report.passed, Is.False);
                Assert.That(
                    report.findings.Any(finding =>
                        finding.category == "RuntimeCodeGeneration"),
                    Is.True);
            }
        }

        [Test]
        public void Scan_FindsDelegateDynamicInvoke()
        {
            using (CecilAssemblyFixture fixture =
                   new CecilAssemblyFixture(
                       "Game.Runtime",
                       referencesNewtonsoft: false))
            {
                TypeDefinition type = fixture.AddType("Game", "Invoker");
                MethodInfo dynamicInvoke = typeof(Delegate).GetMethod(
                    "DynamicInvoke",
                    new[] { typeof(object[]) });
                AddCall(fixture.Module, type, dynamicInvoke);
                string path = fixture.Write();

                DeucarianAotSafetyReport report = Scan(fixture, path);

                Assert.That(report.passed, Is.False);
                Assert.That(
                    report.findings.Any(finding =>
                        finding.category == "ReflectiveInvocation"),
                    Is.True);
            }
        }

        [Test]
        public void Scan_AllowsExactDeclaredAndPreservedException()
        {
            using (CecilAssemblyFixture fixture =
                   new CecilAssemblyFixture(
                       "Vendor.Integration",
                       referencesNewtonsoft: false))
            {
                TypeDefinition type = fixture.AddType("Vendor", "Factory");
                MethodInfo createInstance = typeof(Activator).GetMethod(
                    "CreateInstance",
                    new[] { typeof(Type) });
                AddCall(fixture.Module, type, createInstance, "Create");
                string path = fixture.Write();
                DeucarianAotSafetySettings settings =
                    new DeucarianAotSafetySettings();
                DeucarianAotSafetyException exception =
                    new DeucarianAotSafetyException
                    {
                        assemblyName = "Vendor.Integration",
                        declaringType = "Vendor.Factory",
                        method = "Create",
                        calledApi = "System.Activator::CreateInstance",
                        strategy = "Declared",
                        reason = "Vendor SDK compatibility boundary."
                    };
                exception.preserveTypes.Add(new DeucarianAotPreserveType
                {
                    assemblyName = "Vendor.Integration",
                    typeName = "Vendor.Factory",
                    reason = "Created through the vendor compatibility boundary."
                });
                settings.exceptions.Add(exception);

                DeucarianAotSafetyReport report =
                    DeucarianAotSafetyScanner.Scan(
                        new[] { path },
                        new[] { fixture.DirectoryPath },
                        settings,
                        DeucarianAotSafetyMode.Enforce);

                Assert.That(report.passed, Is.True);
                Assert.That(report.declaredExceptionCount, Is.EqualTo(1));
                Assert.That(report.findings, Is.Empty);
            }
        }

        [Test]
        public void Scan_RejectsExceptionWithoutADeclaredStrategy()
        {
            using (CecilAssemblyFixture fixture =
                   new CecilAssemblyFixture(
                       "Vendor.Integration",
                       referencesNewtonsoft: false))
            {
                TypeDefinition type = fixture.AddType("Vendor", "Factory");
                MethodInfo createInstance = typeof(Activator).GetMethod(
                    "CreateInstance",
                    new[] { typeof(Type) });
                AddCall(fixture.Module, type, createInstance, "Create");
                string path = fixture.Write();
                DeucarianAotSafetySettings settings =
                    new DeucarianAotSafetySettings();
                settings.exceptions.Add(new DeucarianAotSafetyException
                {
                    assemblyName = "Vendor.Integration",
                    declaringType = "Vendor.Factory",
                    method = "Create",
                    calledApi = "System.Activator::CreateInstance",
                    reason = "Incomplete exception."
                });

                DeucarianAotSafetyReport report =
                    DeucarianAotSafetyScanner.Scan(
                        new[] { path },
                        new[] { fixture.DirectoryPath },
                        settings,
                        DeucarianAotSafetyMode.Enforce);

                Assert.That(report.passed, Is.False);
                Assert.That(report.declaredExceptionCount, Is.Zero);
                Assert.That(
                    report.findings.Any(finding =>
                        finding.category == "DynamicConstruction"),
                    Is.True);
            }
        }

        [Test]
        public void Scan_ReadsGeneratedAotFeatureEvidence()
        {
            using (CecilAssemblyFixture fixture =
                   new CecilAssemblyFixture(
                       "Game.Runtime",
                       referencesNewtonsoft: false))
            {
                ConstructorInfo constructor =
                    typeof(AssemblyMetadataAttribute).GetConstructor(
                        new[] { typeof(string), typeof(string) });
                CustomAttribute attribute = new CustomAttribute(
                    fixture.Module.ImportReference(constructor));
                attribute.ConstructorArguments.Add(
                    new CustomAttributeArgument(
                        fixture.Module.TypeSystem.String,
                        "Deucarian.AOT.Feature"));
                attribute.ConstructorArguments.Add(
                    new CustomAttributeArgument(
                        fixture.Module.TypeSystem.String,
                        "serialization-json"));
                fixture.Module.Assembly.CustomAttributes.Add(attribute);
                string path = fixture.Write();

                DeucarianAotSafetyReport report = Scan(fixture, path);

                Assert.That(report.passed, Is.True);
                Assert.That(
                    report.generatedFeatures,
                    Is.EquivalentTo(new[] { "serialization-json" }));
            }
        }

        [Test]
        public void ExecutionScope_RestoresNestedAotMode()
        {
            Assert.That(
                DeucarianBuildExecutionScope.CurrentAotSafetyMode,
                Is.Null);

            using (DeucarianBuildExecutionScope.Enter(
                       DeucarianBuildEnvironment.Production,
                       DeucarianAotSafetyMode.Enforce))
            {
                Assert.That(
                    DeucarianBuildExecutionScope.CurrentAotSafetyMode,
                    Is.EqualTo(DeucarianAotSafetyMode.Enforce));

                using (DeucarianBuildExecutionScope.Enter(
                           DeucarianBuildEnvironment.Development,
                           DeucarianAotSafetyMode.Audit))
                {
                    Assert.That(
                        DeucarianBuildExecutionScope.CurrentAotSafetyMode,
                        Is.EqualTo(DeucarianAotSafetyMode.Audit));
                }

                Assert.That(
                    DeucarianBuildExecutionScope.CurrentAotSafetyMode,
                    Is.EqualTo(DeucarianAotSafetyMode.Enforce));
            }

            Assert.That(
                DeucarianBuildExecutionScope.CurrentAotSafetyMode,
                Is.Null);
        }

        private static DeucarianAotSafetyReport Scan(
            CecilAssemblyFixture fixture,
            string path)
        {
            return DeucarianAotSafetyScanner.Scan(
                new[] { path },
                new[] { fixture.DirectoryPath },
                new DeucarianAotSafetySettings(),
                DeucarianAotSafetyMode.Enforce);
        }

        private static MethodDefinition AddCall(
            ModuleDefinition module,
            TypeDefinition type,
            MethodBase calledMethod,
            string methodName = "Run")
        {
            return AddCall(
                module,
                type,
                module.ImportReference(calledMethod),
                methodName);
        }

        private static MethodDefinition AddCall(
            ModuleDefinition module,
            TypeDefinition type,
            MethodReference calledMethod,
            string methodName = "Run")
        {
            MethodDefinition method = new MethodDefinition(
                methodName,
                Mono.Cecil.MethodAttributes.Public
                | Mono.Cecil.MethodAttributes.Static,
                module.TypeSystem.Void);
            for (int i = 0; i < calledMethod.Parameters.Count; i++)
            {
                method.Body.Instructions.Add(
                    Instruction.Create(OpCodes.Ldnull));
            }

            method.Body.Instructions.Add(
                Instruction.Create(OpCodes.Call, calledMethod));
            if (calledMethod.ReturnType.FullName !=
                module.TypeSystem.Void.FullName)
            {
                method.Body.Instructions.Add(
                    Instruction.Create(OpCodes.Pop));
            }

            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            type.Methods.Add(method);
            return method;
        }
    }
}
