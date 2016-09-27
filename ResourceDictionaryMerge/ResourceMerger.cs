using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace ResourceDictionaryMerge
{
    public class ResourceMerger
    {
        private const string XTypePrefix = "{x:Type ";
        private const string XmlNs = "http://www.w3.org/2000/xmlns/";
        private const string XamlLanguageNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
        private const string ResourceDictionaryName = "ResourceDictionary";

        public void MergeResources(string projectPath, string projectName, string relativeSourceFilePath, string relativeOutputFilePath)
        {
            if (!Directory.Exists(projectPath))
                throw new ArgumentException("Project path does not exist.", nameof(projectPath));

            projectName = string.IsNullOrEmpty(projectName) ? Path.GetFileName(Path.GetDirectoryName(projectPath)) : projectName;

            var sourceFilePath = Path.Combine(projectPath, relativeSourceFilePath);

            if (!File.Exists(sourceFilePath))
                throw new InvalidOperationException("Source file does not exist: " + sourceFilePath);

            var sourceDoc = XDocument.Load(sourceFilePath);

            var sourceRoot = sourceDoc.Root;
            Debug.Assert(sourceRoot != null, "sourceRoot != null");

            var defaultNameSpace = sourceRoot.GetDefaultNamespace();

            var outputDoc = XDocument.Parse("<" + ResourceDictionaryName + " xmlns=\"" + defaultNameSpace + "\"/>");

            var documents = new Dictionary<string, ResourceDictionaryInfo>();
            var namespaces = new Dictionary<string, string>();
            PrepareDocuments(documents, namespaces, projectPath, projectName, relativeSourceFilePath);

            var outputRoot = outputDoc.Root;
            Debug.Assert(outputRoot != null, "outputRoot != null");

            var keys = new Dictionary<string, string>();

            foreach (var item in documents.Values.TopologicalSort(item => item.Dependencies.Select(x => documents[x])))
            {
                var documentRoot = item.Document.Root;
                Debug.Assert(documentRoot != null, "documentRoot != null");

                foreach (var attribute in documentRoot.Attributes())
                    outputRoot.SetAttributeValue(attribute.Name, attribute.Value);

                var children = documentRoot.Elements()
                    .Where(e => !e.Name.LocalName.StartsWith(ResourceDictionaryName));

                foreach (var child in children)
                {
                    var key = GetResourceKey(child);

                    string source;
                    if (keys.TryGetValue(key, out source))
                    {
                        throw new InvalidOperationException(
                            $"Key '{key}' exists both in '{source}' and '{item.Source}'");
                    }

                    keys.Add(key, item.Source);
                    // TODO: adjust namespaces
                    outputRoot.Add(child);
                }
            }

            using (var ms = new MemoryStream())
            {
                outputDoc.Save(ms);
                ms.Position = 0;

                if (IsContentSame(Path.Combine(projectPath, relativeOutputFilePath), ms))
                    return;
            }

            outputDoc.Save(Path.Combine(projectPath, relativeOutputFilePath));
        }

        private static string GetResourceKey(XElement element)
        {
            var key = element.Attribute((XNamespace)XamlLanguageNamespace + "Key")?.Value;
            if (string.IsNullOrEmpty(key))
            {
                if (element.Name.LocalName == "Style")
                {
                    key = element.Attribute("TargetType")?.Value;
                    if (key?.StartsWith(XTypePrefix) == true)
                    {
                        key = key.Substring(XTypePrefix.Length, key.Length - XTypePrefix.Length - 1);
                    }
                }
                else if (element.Name.LocalName == "DataTemplate")
                {
                    key = element.Attribute("DataType")?.Value;
                }
            }

            Trace.Assert(key != null, "key != null");

            return key;
        }

        private static bool IsContentSame(string targetFileName, Stream newStream)
        {
            var normalizedFileName = targetFileName.Replace("/", "\\");

            if (!File.Exists(normalizedFileName))
                return false;

            using (var oldStream = File.OpenRead(normalizedFileName))
            {
                return StreamsContentsAreEqual(newStream, oldStream);
            }
        }

        private static bool StreamsContentsAreEqual(Stream stream1, Stream stream2)
        {
            const int bufferSize = 1024 * sizeof(long);
            var buffer1 = new byte[bufferSize];
            var buffer2 = new byte[bufferSize];

            while (true)
            {
                var count1 = stream1.Read(buffer1, 0, bufferSize);
                var count2 = stream2.Read(buffer2, 0, bufferSize);

                if (count1 != count2)
                {
                    return false;
                }

                if (count1 == 0)
                {
                    return true;
                }

                var iterations = (int)Math.Ceiling((double)count1 / sizeof(long));
                for (var i = 0; i < iterations; i++)
                {
                    if (BitConverter.ToInt64(buffer1, i * sizeof(long)) != BitConverter.ToInt64(buffer2, i * sizeof(long)))
                    {
                        return false;
                    }
                }
            }
        }

        private static void PrepareDocuments(Dictionary<string, ResourceDictionaryInfo> documents, Dictionary<string, string> namespaces, string projectPath, string projectName, string relativeSourceFilePath)
        {
            var absoluteSourceFilePath = Path.Combine(projectPath, relativeSourceFilePath);

            var doc = XDocument.Load(absoluteSourceFilePath);

            var docRoot = doc.Root;
            Debug.Assert(docRoot != null, "docRoot != null");

            ResourceDictionaryInfo resourceDictionaryInfo;
            if (documents.TryGetValue(relativeSourceFilePath, out resourceDictionaryInfo))
            {
                return;
            }

            resourceDictionaryInfo = new ResourceDictionaryInfo(relativeSourceFilePath, doc);
            documents.Add(relativeSourceFilePath, resourceDictionaryInfo);

            AdjustXamlNamespaces(namespaces, docRoot, resourceDictionaryInfo);

            // ReSharper disable once AssignNullToNotNullAttribute
            foreach (var dict in docRoot.Descendants(docRoot.GetDefaultNamespace() + ResourceDictionaryName))
            {
                // ReSharper disable once PossibleNullReferenceException
                var sourceFilePath = BuildPath(projectName, projectPath, absoluteSourceFilePath, dict.Attribute("Source").Value);
                resourceDictionaryInfo.Dependencies.Add(sourceFilePath);
                PrepareDocuments(documents, namespaces, projectPath, projectName,
                    sourceFilePath);
            }
        }

        private static void AdjustXamlNamespaces(Dictionary<string, string> namespaces, XElement docRoot, ResourceDictionaryInfo resourceDictionaryInfo)
        {
            foreach (var attribute in docRoot.Attributes())
            {
                if (attribute.Name.Namespace != XmlNs) continue;

                var name = attribute.Name.LocalName;
                string xamlNamespace;
                if (namespaces.TryGetValue(name, out xamlNamespace))
                {
                    if (attribute.Value != xamlNamespace)
                    {
                        var index = 1;
                        do
                        {
                            name += index++;
                        } while (namespaces.ContainsKey(name));
                        resourceDictionaryInfo.NamespaceMap.Add(attribute.Name.LocalName, name);
                        namespaces.Add(name, attribute.Value);

                        // TODO: handle namespaces
                        throw new InvalidOperationException($"Namespace '{name}' has at least two different definitions ('{xamlNamespace} and '{attribute.Value}').");
                    }
                }
                else
                {
                    namespaces.Add(name, attribute.Value);
                }
            }
        }

        public static string BuildPath(string projectName, string projectPath, string absoluteSourceFilePath, string path)
        {
            var dictionaryPath = path.Replace("pack://application:,,,", string.Empty).Replace("/" + projectName + ";component/", string.Empty);
            var baseUri = new Uri(absoluteSourceFilePath);
            dictionaryPath = new Uri(projectPath).MakeRelativeUri(new Uri(baseUri, dictionaryPath)).ToString();
            return dictionaryPath;
        }

        private class ResourceDictionaryInfo
        {
            public string Source { get; }
            public XDocument Document { get; }
            public List<string> Dependencies { get; }
            public Dictionary<string, string> NamespaceMap { get; }

            public ResourceDictionaryInfo(string source, XDocument document)
            {
                Source = source;
                Document = document;
                Dependencies = new List<string>();
                NamespaceMap = new Dictionary<string, string>();
            }
        }
    }
}