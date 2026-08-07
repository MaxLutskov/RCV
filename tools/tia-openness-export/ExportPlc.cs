using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;

namespace Rcv.TiaOpennessExport
{
    internal static class ExportPlc
    {
        private static int _blocksExported;
        private static int _tagTablesExported;
        private static int _errors;

        private static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.Error.WriteLine("Usage: ExportPlc.exe <output-directory>");
                return 2;
            }

            var outputRoot = new DirectoryInfo(args[0]);
            outputRoot.Create();

            try
            {
                var process = TiaPortal.GetProcesses().FirstOrDefault();
                if (process == null)
                {
                    Console.Error.WriteLine("No running TIA Portal process found.");
                    return 3;
                }

                Console.WriteLine("Attaching to TIA Portal process: " + process.Id);
                using (var tia = process.Attach())
                {
                    var project = tia.Projects.FirstOrDefault();
                    if (project == null)
                    {
                        Console.Error.WriteLine("TIA Portal has no open project.");
                        return 4;
                    }

                    Console.WriteLine("Project: " + project.Name);

                    foreach (var device in project.Devices)
                    {
                        ExportDevice(device, outputRoot);
                    }
                }

                Console.WriteLine("Export complete.");
                Console.WriteLine("Blocks exported: " + _blocksExported);
                Console.WriteLine("Tag tables exported: " + _tagTablesExported);
                Console.WriteLine("Errors: " + _errors);

                return _errors == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        private static void ExportDevice(Device device, DirectoryInfo outputRoot)
        {
            Console.WriteLine("Device: " + SafeName(device.Name));
            foreach (var item in device.DeviceItems)
            {
                ExportDeviceItem(item, outputRoot, SafeName(device.Name));
            }
        }

        private static void ExportDeviceItem(DeviceItem item, DirectoryInfo outputRoot, string pathPrefix)
        {
            var itemName = SafeName(item.Name);
            var nextPrefix = Path.Combine(pathPrefix, itemName);

            var softwareContainer = item.GetService<SoftwareContainer>();
            if (softwareContainer != null)
            {
                var plcSoftware = softwareContainer.Software as PlcSoftware;
                if (plcSoftware != null)
                {
                    var plcDir = new DirectoryInfo(Path.Combine(outputRoot.FullName, nextPrefix));
                    plcDir.Create();
                    Console.WriteLine("PLC software: " + nextPrefix);
                    ExportBlockGroup(plcSoftware.BlockGroup, new DirectoryInfo(Path.Combine(plcDir.FullName, "blocks")));
                    ExportTagTableGroup(plcSoftware.TagTableGroup, new DirectoryInfo(Path.Combine(plcDir.FullName, "tags")));
                }
            }

            foreach (var child in item.DeviceItems)
            {
                ExportDeviceItem(child, outputRoot, nextPrefix);
            }
        }

        private static void ExportBlockGroup(PlcBlockGroup group, DirectoryInfo outputDir)
        {
            outputDir.Create();

            foreach (var block in group.Blocks)
            {
                var file = new FileInfo(Path.Combine(outputDir.FullName, SafeName(block.Name) + ".xml"));
                TryExport("block", block.Name, file, () => block.Export(file, ExportOptions.WithDefaults));
            }

            foreach (var childGroup in group.Groups)
            {
                ExportBlockGroup(childGroup, new DirectoryInfo(Path.Combine(outputDir.FullName, SafeName(childGroup.Name))));
            }
        }

        private static void ExportTagTableGroup(PlcTagTableGroup group, DirectoryInfo outputDir)
        {
            outputDir.Create();

            foreach (var table in group.TagTables)
            {
                var file = new FileInfo(Path.Combine(outputDir.FullName, SafeName(table.Name) + ".xml"));
                TryExport("tag table", table.Name, file, () => table.Export(file, ExportOptions.WithDefaults));
            }

            foreach (var childGroup in group.Groups)
            {
                ExportTagTableGroup(childGroup, new DirectoryInfo(Path.Combine(outputDir.FullName, SafeName(childGroup.Name))));
            }
        }

        private static void TryExport(string kind, string name, FileInfo file, Action export)
        {
            try
            {
                file.Directory.Create();
                export();
                if (kind == "block")
                {
                    _blocksExported++;
                }
                else
                {
                    _tagTablesExported++;
                }
                Console.WriteLine("Exported " + kind + ": " + name + " -> " + file.FullName);
            }
            catch (Exception ex)
            {
                _errors++;
                Console.Error.WriteLine("Failed to export " + kind + " '" + name + "': " + ex.Message);
            }
        }

        private static string SafeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "_";
            }

            var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
            var chars = name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
            return new string(chars);
        }
    }
}
