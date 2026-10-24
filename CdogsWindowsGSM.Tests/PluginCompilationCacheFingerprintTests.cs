using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using WindowsGSM.Functions;

namespace CdogsWindowsGSM.Tests;

[TestClass]
public sealed class PluginCompilationCacheFingerprintTests
{
    [TestMethod]
    public void HostAssemblyContentChangeProducesCacheMissForUnchangedPluginSource()
    {
        var references = PluginManagement.CreatePluginReferences();
        string hostAssemblyPath = Path.GetFullPath(typeof(Plugin).Assembly.Location);
        string hostAssemblyName = typeof(Plugin).Assembly.GetName().Name;
        PluginManagement.PluginCompilationReference hostReference = references.Single(reference =>
            string.Equals(
                reference.Identity,
                hostAssemblyName + "|" + hostAssemblyPath,
                StringComparison.OrdinalIgnoreCase));
        string hostAssemblyHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(hostAssemblyPath)));
        Assert.AreEqual(hostAssemblyHash, hostReference.ContentHash);

        string currentEnvironmentFingerprint = PluginManagement.ComputePluginEnvironmentFingerprint(references);
        var changedReferences = references.Select(reference =>
            ReferenceEquals(reference, hostReference)
                ? new PluginManagement.PluginCompilationReference(
                    reference.Reference,
                    reference.Identity,
                    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("changed host API"))))
                : reference).ToList();
        string changedEnvironmentFingerprint = PluginManagement.ComputePluginEnvironmentFingerprint(changedReferences);

        byte[] sourceBytes = Encoding.UTF8.GetBytes("plugin source is unchanged");
        string sourceFileName = "Example.cs";
        string expectedTypeName = typeof(Plugin).FullName;
        string currentFingerprint = PluginManagement.ComputePluginFingerprint(
            sourceFileName,
            expectedTypeName,
            sourceBytes,
            currentEnvironmentFingerprint);
        string changedFingerprint = PluginManagement.ComputePluginFingerprint(
            sourceFileName,
            expectedTypeName,
            sourceBytes,
            changedEnvironmentFingerprint);
        Assert.AreNotEqual(currentFingerprint, changedFingerprint);

        string cacheRoot = Path.Combine(Path.GetTempPath(), "CdogsWindowsGSM.PluginCacheTests", Guid.NewGuid().ToString("N"));
        byte[] hostAssemblyBytes = File.ReadAllBytes(hostAssemblyPath);
        var cache = new PluginCompilationCache(cacheRoot);
        var diagnostics = new List<string>();

        try
        {
            byte[] publishedAssembly = cache.PublishOrUseExisting(
                sourceFileName,
                currentFingerprint,
                expectedTypeName,
                hostAssemblyBytes,
                diagnostics);
            CollectionAssert.AreEqual(hostAssemblyBytes, publishedAssembly);
            CollectionAssert.AreEqual(
                hostAssemblyBytes,
                cache.TryRead(sourceFileName, currentFingerprint, expectedTypeName, diagnostics));
            Assert.IsNull(cache.TryRead(sourceFileName, changedFingerprint, expectedTypeName, diagnostics));
        }
        finally
        {
            if (Directory.Exists(cacheRoot))
            {
                Directory.Delete(cacheRoot, recursive: true);
            }
        }
    }
}