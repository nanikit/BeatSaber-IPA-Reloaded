using IPA.Loader;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace IPA.Loader.Tests
{
    [TestClass, DoNotParallelize]
    public class CecilLibLoaderTests
    {
        private string directory;
        private Dictionary<string, (string Path, Version Version)> previousLocations;

        [TestInitialize]
        public void CreateLibraryDirectory()
        {
            directory = Path.Combine(Path.GetTempPath(), "BSIPA-Resolver-" + Guid.NewGuid());
            Directory.CreateDirectory(directory);
            previousLocations = LibLoader.FilenameLocations;
            LibLoader.FilenameLocations = new Dictionary<string, (string, Version)>();
        }

        [TestCleanup]
        public void RestoreLibraryDirectory()
        {
            LibLoader.FilenameLocations = previousLocations;
            Directory.Delete(directory, true);
        }

        [TestMethod]
        public void RepeatedEnumAttributesReadTheirDependencyOnce()
        {
            string path = CreateAttributeConsumer();
            LibLoader.FilenameLocations["System.dll"] = (typeof(EditorBrowsableAttribute).Assembly.Location, new Version(4, 0, 0, 0));
            using var resolver = new CountingResolver();
            using var consumer = AssemblyDefinition.ReadAssembly(path, new ReaderParameters
            {
                ReadingMode = ReadingMode.Immediate,
                AssemblyResolver = resolver
            });

            Assert.AreEqual(64, consumer.MainModule.Types.Count(type => type.HasCustomAttributes));
            Assert.IsTrue(consumer.MainModule.Types.Where(type => type.HasCustomAttributes)
                .All(type => (int)type.CustomAttributes[0].ConstructorArguments[0].Value == (int)EditorBrowsableState.Never));
            Assert.AreEqual(1, resolver.SystemReads);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void RepeatedLookupsKeepLibraryPathPrecedence(bool versionedOnly)
        {
            string plain = CreateLibrary("plain.dll");
            string versioned = CreateLibrary("versioned.dll");
            if (!versionedOnly)
                LibLoader.FilenameLocations["Dependency.dll"] = (plain, new Version(1, 0, 0, 0));
            LibLoader.FilenameLocations["Dependency.1.0.0.0.dll"] = (versioned, new Version(1, 0, 0, 0));
            using var resolver = new CecilLibLoader();
            using var first = resolver.Resolve(DependencyName());
            using var second = resolver.Resolve(DependencyName());

            Assert.AreEqual(versionedOnly ? versioned : plain, first.MainModule.FileName);
            Assert.AreSame(first, second);
        }

        [TestMethod]
        public void LoaderAssemblyLookupsShareTheirMetadata()
        {
            using var resolver = new CecilLibLoader();
            var name = AssemblyNameReference.Parse(typeof(CecilLibLoader).Assembly.FullName);
            using var first = resolver.Resolve(name);
            using var second = resolver.Resolve(name);

            Assert.AreEqual(typeof(CecilLibLoader).Assembly.Location, first.MainModule.FileName);
            Assert.AreSame(first, second);
        }

        [TestMethod]
        public void ExplicitReadOptionsStillCreateAnIndependentRead()
        {
            LibLoader.FilenameLocations["Dependency.dll"] = (CreateLibrary("dependency.dll"), new Version(1, 0, 0, 0));
            using var resolver = new CecilLibLoader();
            using var cached = resolver.Resolve(DependencyName());
            using var explicitRead = resolver.Resolve(DependencyName(), new ReaderParameters { InMemory = true });

            Assert.AreNotSame(cached, explicitRead);
            Assert.AreEqual(cached.Name.FullName, explicitRead.Name.FullName);
        }

        [TestMethod]
        public void FinishingMetadataLoadingReleasesDependencyFiles()
        {
            string path = CreateLibrary("dependency.dll");
            LibLoader.FilenameLocations["Dependency.dll"] = (path, new Version(1, 0, 0, 0));
            using var resolver = new CecilLibLoader();
            using var resolved = resolver.Resolve(DependencyName());
            resolver.Dispose();

            using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.IsTrue(exclusive.Length > 0);
        }

        private string CreateLibrary(string fileName)
        {
            string path = Path.Combine(directory, fileName);
            using var assembly = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition("Dependency", new Version(1, 0, 0, 0)), "Dependency", ModuleKind.Dll);
            assembly.Write(path);
            return path;
        }

        private string CreateAttributeConsumer()
        {
            string path = Path.Combine(directory, "Consumer.dll");
            using var assembly = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition("Consumer", new Version(1, 0)), "Consumer", ModuleKind.Dll);
            var module = assembly.MainModule;
            var constructor = module.ImportReference(typeof(EditorBrowsableAttribute).GetConstructor(new[] { typeof(EditorBrowsableState) }));
            var enumType = module.ImportReference(typeof(EditorBrowsableState));
            for (int index = 0; index < 64; index++)
            {
                var type = new TypeDefinition("Fixture", "Type" + index, TypeAttributes.Public, module.TypeSystem.Object);
                var attribute = new CustomAttribute(constructor);
                attribute.ConstructorArguments.Add(new CustomAttributeArgument(enumType, (int)EditorBrowsableState.Never));
                type.CustomAttributes.Add(attribute);
                module.Types.Add(type);
            }
            assembly.Write(path);
            return path;
        }

        private static AssemblyNameReference DependencyName() => new AssemblyNameReference("Dependency", new Version(1, 0, 0, 0));

        private sealed class CountingResolver : CecilLibLoader
        {
            public int SystemReads { get; private set; }

            public override AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
            {
                if (name.Name == "System")
                    SystemReads++;
                return base.Resolve(name, parameters);
            }
        }
    }
}
