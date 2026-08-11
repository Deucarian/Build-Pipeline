using System;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NUnit.Framework;

namespace Deucarian.BuildPipeline.Tests
{
    public sealed class DeucarianAotAssemblyEvidenceTests
    {
        [Test]
        public void PackageOwnedDeclaredExceptionRequiresPreserveMetadata()
        {
            using (CecilAssemblyFixture fixture =
                   new CecilAssemblyFixture(
                       "Vendor.Integration",
                       referencesNewtonsoft: false))
            {
                TypeDefinition type = fixture.AddType("Vendor", "Factory");
                AddActivatorCall(fixture.Module, type);
                AddMetadata(
                    fixture.Module,
                    DeucarianAotAssemblyEvidence.ExceptionMetadataKey,
                    "Vendor.Factory|Create|System.Activator::CreateInstance|Declared|Vendor compatibility boundary.");
                string withoutPreservePath = fixture.Write("without-preserve.dll");

                DeucarianAotSafetyReport withoutPreserve = Scan(
                    fixture,
                    withoutPreservePath);

                Assert.That(withoutPreserve.passed, Is.False);
                Assert.That(
                    withoutPreserve.findings.Any(finding =>
                        finding.category == "DynamicConstruction"),
                    Is.True);

                AddMetadata(
                    fixture.Module,
                    DeucarianAotAssemblyEvidence.PreserveTypeMetadataKey,
                    "Vendor.Integration|Vendor.Factory|Created by the vendor boundary.");
                string withPreservePath = fixture.Write("with-preserve.dll");

                DeucarianAotSafetyReport withPreserve = Scan(
                    fixture,
                    withPreservePath);

                Assert.That(withPreserve.passed, Is.True);
                Assert.That(withPreserve.declaredExceptionCount, Is.EqualTo(1));
            }
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

        private static void AddActivatorCall(
            ModuleDefinition module,
            TypeDefinition type)
        {
            MethodInfo createInstance = typeof(Activator).GetMethod(
                "CreateInstance",
                new[] { typeof(Type) });
            MethodReference calledMethod =
                module.ImportReference(createInstance);
            MethodDefinition method = new MethodDefinition(
                "Create",
                Mono.Cecil.MethodAttributes.Public
                | Mono.Cecil.MethodAttributes.Static,
                module.TypeSystem.Void);
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldnull));
            method.Body.Instructions.Add(
                Instruction.Create(OpCodes.Call, calledMethod));
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Pop));
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            type.Methods.Add(method);
        }

        private static void AddMetadata(
            ModuleDefinition module,
            string key,
            string value)
        {
            ConstructorInfo constructor =
                typeof(AssemblyMetadataAttribute).GetConstructor(
                    new[] { typeof(string), typeof(string) });
            CustomAttribute attribute = new CustomAttribute(
                module.ImportReference(constructor));
            attribute.ConstructorArguments.Add(
                new CustomAttributeArgument(
                    module.TypeSystem.String,
                    key));
            attribute.ConstructorArguments.Add(
                new CustomAttributeArgument(
                    module.TypeSystem.String,
                    value));
            module.Assembly.CustomAttributes.Add(attribute);
        }
    }
}
