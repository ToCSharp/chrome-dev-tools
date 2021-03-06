﻿namespace BaristaLabs.ChromeDevTools.RemoteInterface.CodeGen
{
    using HandlebarsDotNet;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Humanizer;
    using BaristaLabs.ChromeDevTools.RemoteInterface.ProtocolDefinition;

    /// <summary>
    /// Represents a class that manages templates and their associated generators.
    /// </summary>
    public sealed class TemplatesManager
    {
        private readonly IDictionary<string, Func<object, string>> m_templateGenerators = new Dictionary<string, Func<object, string>>(StringComparer.OrdinalIgnoreCase);
        private readonly CodeGenerationSettings m_settings;

        /// <summary>
        /// Gets the code generation settings associated with the protocol generator
        /// </summary>
        public CodeGenerationSettings Settings
        {
            get { return m_settings; }
        }

        public TemplatesManager(CodeGenerationSettings settings)
        {
            m_settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <summary>
        /// Returns a generator singleton for the specified template path.
        /// </summary>
        /// <param name="templatePath"></param>
        /// <returns></returns>
        public Func<object, string> GetGeneratorForTemplate(CodeGenerationTemplateSettings templateSettings)
        {
            var templatePath = templateSettings.TemplatePath;
            if (m_templateGenerators.ContainsKey(templatePath))
                return m_templateGenerators[templatePath];

            var targetTemplate = templatePath;
            if (!Path.IsPathRooted(targetTemplate))
                targetTemplate = Path.Combine(Settings.TemplatesPath, targetTemplate);

            if (!File.Exists(targetTemplate))
                throw new FileNotFoundException($"Unable to locate a template at {targetTemplate} - please ensure that a template file exists at this location.");

            var templateContents = File.ReadAllText(targetTemplate);

            Handlebars.RegisterHelper("dehumanize", (writer, context, arguments) =>
            {
                if (arguments.Length != 1)
                {
                    throw new HandlebarsException("{{humanize}} helper must have exactly one argument");
                }

                var str = arguments[0].ToString();

                //Some overrides for values that start with '-' -- this fixes two instances in Runtime.UnserializableValue
                if (str.StartsWith("-"))
                {
                    str = $"Negative{str.Dehumanize()}";
                }
                else
                {
                    str = str.Dehumanize();
                }

                writer.WriteSafeString(str.Dehumanize());
            });

            Handlebars.RegisterHelper("typemap", (writer, context, arguments) =>
            {
                var typeDefinition = context as TypeDefinition;
                if (typeDefinition == null)
                {
                    throw new HandlebarsException("{{typemap}} helper expects to be in the context of a TypeDefinition.");
                }

                if (arguments.Length != 1)
                {
                    throw new HandlebarsException("{{typemap}} helper expects exactly one argument - the CodeGeneratorContext.");
                }

                var codeGenContext = arguments[0] as CodeGeneratorContext;
                if (codeGenContext == null)
                    throw new InvalidOperationException("Expected context argument to be non-null.");

                var mappedType = Utility.GetTypeMappingForType(typeDefinition, codeGenContext.Domain, codeGenContext.KnownTypes);
                writer.WriteSafeString(mappedType);
            });

            return Handlebars.Compile(templateContents);
        }
    }
}
