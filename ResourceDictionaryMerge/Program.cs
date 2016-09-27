using System;
using System.Diagnostics.CodeAnalysis;
using CommandLine;
using CommandLine.Text;

namespace ResourceDictionaryMerge
{
    internal class Program
    {
        [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Local")]
        private class Options
        {
            [Option('p', Required = true, HelpText = "Project path")]
            public string ProjectPath { get; set; }

            [Option('n', Required = true, HelpText = "Project name")]
            public string ProjectName { get; set; }

            [Option('s', Required = true, HelpText = "Source path")]
            public string SourcePath { get; set; }

            [Option('t', Required = true, HelpText = "Target path")]
            public string TargetPath { get; set; }
        }

        private static int Main(string[] args)
        {
            var result = Parser.Default.ParseArguments<Options>(args);
            var parsed = result as Parsed<Options>;
            if (parsed == null)
            {
                Console.Error.WriteLine(HelpText.RenderUsageText(result));
                return 1;
            }

            var options = parsed.Value;
            //var options = new Options
            //{
            //    ProjectName = "ResourceMergeDemo",
            //    ProjectPath = @"C:\Source\Repos\ResourceDictionaryMerge\ResourceMergeDemo\",
            //    SourcePath = "Bundle.xaml",
            //    TargetPath = "Bundle.Merged.xaml"
            //};
            new ResourceMerger().MergeResources(options.ProjectPath, options.ProjectName, options.SourcePath, options.TargetPath);
            return 0;
        }
    }
}
