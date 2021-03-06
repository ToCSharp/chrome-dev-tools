﻿namespace RemoteInterfaceGeneratorCLI
{
    using BaristaLabs.ChromeDevTools;
    using BaristaLabs.ChromeDevTools.RemoteInterface.CodeGen;
    using BaristaLabs.ChromeDevTools.RemoteInterface.ProtocolDefinition;
    using EntryPoint;
    using Microsoft.Extensions.DependencyInjection;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using NJsonSchema;
    using NJsonSchema.Generation;
    using System;
    using System.IO;
    using System.Threading.Tasks;
    using System.Text;

    class Program
    {
        static int Main(string[] args)
        {
            var cliArguments = Cli.Parse<CliArguments>(args);

            //Load Settings.
            if (!File.Exists(cliArguments.Settings))
                throw new FileNotFoundException($"The specified settings file ({cliArguments.Settings}) could not be found. Please check that the settings file exists.");

            var settingsJson = File.ReadAllText(cliArguments.Settings);
            var settings = JsonConvert.DeserializeObject<CodeGenerationSettings>(settingsJson);

            // setup our DI
            var serviceProvider = new ServiceCollection()
                .AddCodeGenerationServices(settings)
                .BuildServiceProvider();

            //Get the protocol Data.
            Console.WriteLine("Loading protocol definition...");
            var protocolDefinitionData = GetProtocolDefinitionData(cliArguments).GetAwaiter().GetResult();

            //Validate that the protocol data matches our current class object.
            var protocolSchema = JsonSchema4.FromTypeAsync<ProtocolDefinition>(new JsonSchemaGeneratorSettings() { FlattenInheritanceHierarchy = true }).GetAwaiter().GetResult();
            var errors = protocolSchema.Validate(protocolDefinitionData);
            if (errors.Count > 0)
                throw new InvalidOperationException("Protocol Definition data does not validate against Protocol Definition Class Library. Ensure that all properties have been added.");

            var protocolDefinition = protocolDefinitionData.ToObject<ProtocolDefinition>(new JsonSerializer() { MetadataPropertyHandling = MetadataPropertyHandling.Ignore });

            //Begin the code generation process.
            Console.WriteLine("Generating protocol definition code files...");
            var protocolGenerator = serviceProvider.GetRequiredService<ICodeGenerator<ProtocolDefinition>>();
            var codeFiles = protocolGenerator.GenerateCode(protocolDefinition, null);

            //Delete the output folder if force is specified and it exists...
            Console.WriteLine("Writing generated code files...");
            if (Directory.Exists(cliArguments.OutputPath) && cliArguments.ForceOverwrite)
            {
                Console.WriteLine("Generating protocol definition project...");
                Directory.Delete(cliArguments.OutputPath, true);
            }

            //Create the output path if it doesn't exist, and write generated files to disk.
            var directoryInfo = Directory.CreateDirectory(cliArguments.OutputPath);

            var sha1 = System.Security.Cryptography.SHA1.Create();
            foreach (var codeFile in codeFiles)
            {
                var targetFilePath = Path.GetFullPath(Path.Combine(cliArguments.OutputPath, codeFile.Key));
                Directory.CreateDirectory(Path.GetDirectoryName(targetFilePath));
                //Only update the file if the SHA1 hashes don't match
                if (File.Exists(targetFilePath))
                {
                    var targetFileHash = sha1.ComputeHash(File.ReadAllBytes(targetFilePath));
                    var codeFileHash = sha1.ComputeHash(Encoding.UTF8.GetBytes(codeFile.Value));
                    if (String.Compare(Convert.ToBase64String(targetFileHash), Convert.ToBase64String(codeFileHash)) != 0)
                    {
                        File.WriteAllText(targetFilePath, codeFile.Value);
                    }
                }
                else
                {
                    File.WriteAllText(targetFilePath, codeFile.Value);
                }
            }

            //Completed.
            Console.WriteLine("All done!");
            return 0;
        }

        /// <summary>
        /// Returns a merged ProtocolDefinition JObject
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        public static async Task<JObject> GetProtocolDefinitionData(CliArguments args)
        {
            JObject protocolData;

            if (args.ForceDownload || !File.Exists(args.ProtocolPath))
            {
                Console.WriteLine("Obtaining protocol definition from installed Chrome version...");

                using (var chrome = Chrome.OpenChrome())
                {
                    protocolData = await chrome.GetProtocolDefinitionForCurrentChromeVersion();
                }

                File.WriteAllText(args.ProtocolPath, JsonConvert.SerializeObject(protocolData, Formatting.Indented));
            }
            else
            {
                var protocolJson = File.ReadAllText(args.ProtocolPath);
                protocolData = JObject.Parse(protocolJson);
            }

            return protocolData;
        }
    }
}