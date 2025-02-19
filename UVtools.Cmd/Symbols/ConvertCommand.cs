﻿/*
 *                     GNU AFFERO GENERAL PUBLIC LICENSE
 *                       Version 3, 19 November 2007
 *  Copyright (C) 2007 Free Software Foundation, Inc. <https://fsf.org/>
 *  Everyone is permitted to copy and distribute verbatim copies
 *  of this license document, but changing it is not allowed.
 */
using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using UVtools.Core.Extensions;
using UVtools.Core.FileFormats;

namespace UVtools.Cmd.Symbols;

internal static class ConvertCommand
{
    internal static Command CreateCommand()
    {
        var command = new Command("convert", "Convert input file into a output file format by a known type or extension")
        {
            GlobalArguments.InputFileArgument,
            new Argument<string>("target-type/ext", "Target format type or extension"),
            GlobalArguments.OutputFileArgument,

            new Option<ushort>(new[] {"-v", "--version"}, "Sets the file format version"),
            new Option<bool>("--no-overwrite", "If the output file exists do not overwrite"),
        };

        command.SetHandler((FileInfo inputFile, string targetTypeExt, FileInfo? outputFile, ushort version, bool noOverwrite) =>
            {
                var targetType = FileFormat.FindByType(targetTypeExt);
                if (targetType is null)
                {
                    targetType = FileFormat.FindByExtensionOrFilePath(targetTypeExt, out var fileFormatsSharingExt);
                    if (targetType is not null && fileFormatsSharingExt > 1)
                    {
                        Program.WriteLineError($"The extension '{targetTypeExt}' is shared by multiple encoders, use the strict encoder name instead.", false);
                        Program.WriteLineError($"Available {FileFormat.AvailableFormats.Length} encoders:", false, false);
                        foreach (var fileFormat in FileFormat.AvailableFormats)
                        {
                            Program.WriteLineError($"{fileFormat.GetType().Name.RemoveFromEnd("File".Length)} ({string.Join(", ", fileFormat.FileExtensions.Select(extension => extension.Extension))})", false, false);
                        }
                        Environment.Exit(-1);
                        return;
                    }
                }

                if (targetType is null)
                {
                    Program.WriteLineError($"Unable to find a valid encoder from {targetTypeExt}.", false);
                    Program.WriteLineError($"Available {FileFormat.AvailableFormats.Length} encoders:", false, false);
                    foreach (var fileFormat in FileFormat.AvailableFormats)
                    {
                        Program.WriteLineError($"{fileFormat.GetType().Name.RemoveFromEnd("File".Length)} ({string.Join(", ", fileFormat.FileExtensions.Select(extension => extension.Extension))})", false, false);
                    }
                    Environment.Exit(-1);
                    return;
                }

                string? outputFilePath;
                string inputFileName = FileFormat.GetFileNameStripExtensions(inputFile.Name)!;
                if (outputFile is null)
                {
                    outputFilePath = inputFileName;
                    if (targetType.FileExtensions.Length == 1)
                    {
                        outputFilePath = Path.Combine(inputFile.DirectoryName!, $"{outputFilePath}.{targetType.FileExtensions[0].Extension}");
                    }
                    else
                    {
                        var ext = FileExtension.Find(targetTypeExt);
                        if (ext is null)
                        {
                            Program.WriteLineError($"Unable to construct the output filename and guess the extension from the {targetTypeExt} encoder.", false);
                            Program.WriteLineError($"There are {targetType.FileExtensions.Length} possible extensions on this format ({string.Join(", ", targetType.FileExtensions.Select(extension => extension.Extension))}), please specify an output file.", false, false);
                            return;
                        }

                        outputFilePath = Path.Combine(inputFile.DirectoryName!, $"{outputFilePath}.{ext.Extension}");
                    }
                }
                else
                {
                    outputFilePath = string.Format(outputFile.FullName, inputFileName);
                }

                var outputFileName = Path.GetFileName(outputFilePath);
                var outputFileDirectory = Path.GetDirectoryName(outputFilePath)!;

                if (outputFileName == string.Empty)
                {
                    Program.WriteLineError("No output file was specified.");
                    return;
                }

                if (!outputFileName.Contains('.'))
                {
                    if (targetType.IsExtensionValid(outputFileName))
                    {
                        outputFileName = $"{inputFileName}.{outputFileName}";
                        outputFilePath = Path.Combine(outputFileDirectory, outputFileName);
                    }
                    else if (targetType.FileExtensions.Length == 1)
                    {
                        outputFileName = $"{outputFileName}.{targetType.FileExtensions[0].Extension}";
                        outputFilePath = Path.Combine(outputFileDirectory, outputFileName);
                    }
                }

                if (!targetType.IsExtensionValid(outputFileName, true))
                {
                    Program.WriteLineError($"The extension on '{outputFileName}' file is not valid for the {targetType.GetType().Name} encoder.", false);
                    Program.WriteLineError($"Available {targetType.FileExtensions.Length} extension(s):", false, false);
                    foreach (var fileExtension in targetType.FileExtensions)
                    {
                        Program.WriteLineError(fileExtension.Extension, false, false);
                    }

                    Environment.Exit(-1);

                    return;
                }

                if (noOverwrite && File.Exists(outputFilePath))
                {
                    Program.WriteLineError($"{outputFileName} already exits! --no-overwrite is enabled.");
                    return;
                }

                var slicerFile = Program.OpenInputFile(inputFile);

                Program.ProgressBarWork($"Converting to {outputFileName}",
                    () =>
                    {
                        try
                        {
                            return slicerFile.Convert(targetType, outputFilePath, version, Program.Progress);
                        }
                        catch (Exception)
                        {
                            File.Delete(outputFilePath);
                            throw;
                        }
                    });

            }, command.Arguments[0], command.Arguments[1], command.Arguments[2],
            command.Options[0], command.Options[1]);

        return command;
    }
}