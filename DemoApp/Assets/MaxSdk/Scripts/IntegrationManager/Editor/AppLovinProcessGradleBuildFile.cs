﻿//  AppLovin MAX Unity Plugin
//
//  Created by Santosh Bagadi on 9/3/19.
//  Copyright © 2019 AppLovin. All rights reserved.
//

#if UNITY_ANDROID

using System.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

[Serializable]
public class AppLovinQualityServiceData
{
    public string api_key;
}

/// <summary>
/// Adds or updates the AppLovin Quality Service plugin to the provided build.gradle file.
/// If the gradle file already has the plugin, the API key is updated.
/// </summary>
public abstract class AppLovinProcessGradleBuildFile
{
    private static readonly Regex TokenBuildScriptRepositories = new Regex(".*jcenter().*");
    private static readonly Regex TokenBuildScriptDependencies = new Regex(".*classpath \'com.android.tools.build:gradle.*");
    private static readonly Regex TokenApplicationPlugin = new Regex(".*apply plugin: \'com.android.application\'.*");
    private static readonly Regex TokenApiKey = new Regex(".*apiKey.*");
    private static readonly Regex TokenAppLovinPlugin = new Regex(".*apply plugin:.+?(?=applovin-quality-service).*");

    private const string BuildScriptMatcher = "buildscript";
    private const string QualityServiceMavenRepo = "maven { url 'https://applovin.bintray.com/Quality-Service' }";
    private const string QualityServiceDependencyClassPath = "classpath 'com.applovin.quality:AppLovinQualityServiceGradlePlugin:3.+'";
    private const string QualityServiceApplyPlugin = "apply plugin: 'applovin-quality-service'";
    private const string QualityServicePlugin = "applovin {";
    private const string QualityServiceApiKey = "    apiKey '{0}'";

    // Legacy plugin detection variables
    private static readonly Regex TokenSafeDkLegacyApplyPlugin = new Regex(".*apply plugin:.+?(?=safedk).*");
    private const string SafeDkLegacyPlugin = "safedk {";
    private const string SafeDkLegacyMavenRepo = "http://download.safedk.com";
    private const string SafeDkLegacyDependencyClassPath = "com.safedk:SafeDKGradlePlugin:";

    /// <summary>
    /// Updates the provided Gradle script to add Quality Service plugin.
    /// </summary>
    /// <param name="applicationGradleBuildFilePath">The gradle file to update.</param>
    protected void AddAppLovinQualityServicePlugin(string applicationGradleBuildFilePath)
    {
        if (!AppLovinSettings.Instance.QualityServiceEnabled) return;

        var sdkKey = AppLovinSettings.Instance.SdkKey;
        if (string.IsNullOrEmpty(sdkKey))
        {
            MaxSdkLogger.UserError("Failed to install AppLovin Quality Service plugin. SDK Key is empty. Please enter the AppLovin SDK Key in the Integration Manager.");
            return;
        }

        // Retrieve the API Key using the SDK Key.
        var qualityServiceData = RetrieveQualityServiceData(sdkKey);
        var apiKey = qualityServiceData.api_key;
        if (string.IsNullOrEmpty(apiKey))
        {
            MaxSdkLogger.UserError("Failed to install AppLovin Quality Service plugin. API Key is empty.");
            return;
        }

        // Generate the updated Gradle file that needs to be written.
        var lines = File.ReadAllLines(applicationGradleBuildFilePath).ToList();
        var sanitizedLines = RemoveLegacySafeDkPlugin(lines);
        var outputLines = GenerateUpdatedBuildFileLines(
            sanitizedLines,
            apiKey,
#if UNITY_2019_3_OR_NEWER
            false // On Unity 2019.3+, the buildscript closure related lines will to be added to the root build.gradle file.
#else
            true
#endif
        );
        // outputLines can be null if we couldn't add the plugin. 
        if (outputLines == null) return;

        try
        {
            File.WriteAllText(applicationGradleBuildFilePath, string.Join("\n", outputLines.ToArray()) + "\n");
        }
        catch (Exception exception)
        {
            MaxSdkLogger.UserError("Failed to install AppLovin Quality Service plugin. Gradle file write failed.");
            Console.WriteLine(exception);
        }
    }

#if UNITY_2019_3_OR_NEWER
    /// <summary>
    /// Adds the necessary AppLovin Quality Service dependency and maven repo lines to the provided root build.gradle file.
    /// </summary>
    /// <param name="rootGradleBuildFile">The root build.gradle file path</param>
    /// <returns><c>true</c> if the build script lines were applied correctly.</returns>
    protected bool AddQualityServiceBuildScriptLines(string rootGradleBuildFile)
    {
        var lines = File.ReadAllLines(rootGradleBuildFile).ToList();
        var outputLines = GenerateUpdatedBuildFileLines(lines, null, true);

        // outputLines will be null if we couldn't add the build script lines.
        if (outputLines == null) return false;

        try
        {
            File.WriteAllText(rootGradleBuildFile, string.Join("\n", outputLines.ToArray()) + "\n");
        }
        catch (Exception exception)
        {
            MaxSdkLogger.UserError("Failed to install AppLovin Quality Service plugin. Root Gradle file write failed.");
            Console.WriteLine(exception);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Removes the AppLovin Quality Service Plugin or Legacy SafeDK plugin from the given gradle template file if either of them are present.
    /// </summary>
    /// <param name="gradleTemplateFile">The gradle template file from which to remove the plugin from</param>
    protected static void RemoveAppLovinQualityServiceOrSafeDkPlugin(string gradleTemplateFile)
    {
        var lines = File.ReadAllLines(gradleTemplateFile).ToList();
        lines = RemoveLegacySafeDkPlugin(lines);
        lines = RemoveAppLovinQualityServicePlugin(lines);

        try
        {
            File.WriteAllText(gradleTemplateFile, string.Join("\n", lines.ToArray()) + "\n");
        }
        catch (Exception exception)
        {
            MaxSdkLogger.UserError("Failed to remove AppLovin Quality Service Plugin from mainTemplate.gradle. Please remove the Quality Service plugin from the mainTemplate.gradle manually.");
            Console.WriteLine(exception);
        }
    }
#endif

    private static AppLovinQualityServiceData RetrieveQualityServiceData(string sdkKey)
    {
        var postJson = string.Format("{{\"sdk_key\" : \"{0}\"}}", sdkKey);
        var bodyRaw = Encoding.UTF8.GetBytes(postJson);
        var uploadHandler = new UploadHandlerRaw(bodyRaw);
        uploadHandler.contentType = "application/json";

        var unityWebRequest = new UnityWebRequest("https://api2.safedk.com/v1/build/cred")
        {
            method = UnityWebRequest.kHttpVerbPOST,
            uploadHandler = uploadHandler,
            downloadHandler = new DownloadHandlerBuffer()
        };

#if UNITY_2017_2_OR_NEWER
        var operation = unityWebRequest.SendWebRequest();
#else
        var operation = webRequest.Send();
#endif

        // Wait for the download to complete or the request to timeout.
        while (!operation.isDone) { }

#if UNITY_2017_2_OR_NEWER
        if (unityWebRequest.isNetworkError || unityWebRequest.isHttpError)
#else
        if (webRequest.isError)
#endif
        {
            MaxSdkLogger.UserError("Failed to retrieve API Key for SDK Key: " + sdkKey + "with error: " + unityWebRequest.error);
            return new AppLovinQualityServiceData();
        }

        try
        {
            return JsonUtility.FromJson<AppLovinQualityServiceData>(unityWebRequest.downloadHandler.text);
        }
        catch (Exception exception)
        {
            MaxSdkLogger.UserError("Failed to parse API Key." + exception);
            return new AppLovinQualityServiceData();
        }
    }

    private static List<string> RemoveLegacySafeDkPlugin(List<string> lines)
    {
        return RemovePlugin(lines, SafeDkLegacyPlugin, SafeDkLegacyMavenRepo, SafeDkLegacyDependencyClassPath, TokenSafeDkLegacyApplyPlugin);
    }

    private static List<string> RemoveAppLovinQualityServicePlugin(List<string> lines)
    {
        return RemovePlugin(lines, QualityServicePlugin, QualityServiceMavenRepo, QualityServiceDependencyClassPath, TokenAppLovinPlugin);
    }

    private static List<string> RemovePlugin(List<string> lines, string pluginLine, string mavenRepo, string dependencyClassPath, Regex applyPluginToken)
    {
        var sanitizedLines = new List<string>();
        var legacyRepoRemoved = false;
        var legacyDependencyClassPathRemoved = false;
        var legacyPluginRemoved = false;
        var legacyPluginMatched = false;
        var insideLegacySafeDkClosure = false;
        foreach (var line in lines)
        {
            if (!legacyPluginMatched && line.Contains(pluginLine))
            {
                legacyPluginMatched = true;
                insideLegacySafeDkClosure = true;
            }

            if (insideLegacySafeDkClosure && line.Contains("}"))
            {
                insideLegacySafeDkClosure = false;
                continue;
            }

            if (insideLegacySafeDkClosure)
            {
                continue;
            }

            if (!legacyRepoRemoved && line.Contains(mavenRepo))
            {
                legacyRepoRemoved = true;
                continue;
            }

            if (!legacyDependencyClassPathRemoved && line.Contains(dependencyClassPath))
            {
                legacyDependencyClassPathRemoved = true;
                continue;
            }

            if (!legacyPluginRemoved && applyPluginToken.IsMatch(line))
            {
                legacyPluginRemoved = true;
                continue;
            }

            sanitizedLines.Add(line);
        }

        return sanitizedLines;
    }

    private static List<string> GenerateUpdatedBuildFileLines(List<string> lines, string apiKey, bool addBuildScriptLines)
    {
        var addPlugin = !string.IsNullOrEmpty(apiKey);
        // A sample of the template file.
        // ...
        // allprojects {
        //     repositories {**ARTIFACTORYREPOSITORY**
        //         google()
        //         jcenter()
        //         flatDir {
        //             dirs 'libs'
        //         }
        //     }
        // }
        //
        // apply plugin: 'com.android.application'
        //     **APPLY_PLUGINS**
        //
        // dependencies {
        //     implementation fileTree(dir: 'libs', include: ['*.jar'])
        //     **DEPS**}
        // ...
        var outputLines = new List<string>();
        // Check if the plugin exists, if so, update the SDK Key.
        var pluginExists = lines.Any(line => TokenAppLovinPlugin.IsMatch(line));
        if (pluginExists)
        {
            var pluginMatched = false;
            var insideAppLovinClosure = false;
            var updatedApiKey = false;
            foreach (var line in lines)
            {
                if (!pluginMatched && line.Contains(QualityServicePlugin))
                {
                    insideAppLovinClosure = true;
                    pluginMatched = true;
                }

                if (insideAppLovinClosure && line.Contains("}"))
                {
                    insideAppLovinClosure = false;
                }

                // Update the API key.
                if (insideAppLovinClosure && !updatedApiKey && TokenApiKey.IsMatch(line))
                {
                    outputLines.Add(string.Format(QualityServiceApiKey, apiKey));
                    updatedApiKey = true;
                }
                // Keep adding the line until we find and update the plugin.
                else
                {
                    outputLines.Add(line);
                }
            }
        }
        // Plugin hasn't been added yet, add it.
        else
        {
            var buildScriptClosureDepth = 0;
            var insideBuildScriptClosure = false;
            var buildScriptMatched = false;
            var qualityServiceRepositoryAdded = false;
            var qualityServiceDependencyClassPathAdded = false;
            var qualityServicePluginAdded = false;
            foreach (var line in lines)
            {
                // Add the line to the output lines.
                outputLines.Add(line);

                // Check if we need to add the build script lines and add them.
                if (addBuildScriptLines)
                {
                    if (!buildScriptMatched && line.Contains(BuildScriptMatcher))
                    {
                        buildScriptMatched = true;
                        insideBuildScriptClosure = true;
                    }

                    // Match the parenthesis to track if we are still inside the buildscript closure.
                    if (insideBuildScriptClosure)
                    {
                        if (line.Contains("{"))
                        {
                            buildScriptClosureDepth++;
                        }

                        if (line.Contains("}"))
                        {
                            buildScriptClosureDepth--;
                        }

                        if (buildScriptClosureDepth == 0)
                        {
                            insideBuildScriptClosure = false;

                            // There may be multiple buildscript closures and we need to keep looking until we added both the repository and classpath.
                            buildScriptMatched = qualityServiceRepositoryAdded && qualityServiceDependencyClassPathAdded;
                        }
                    }

                    if (insideBuildScriptClosure)
                    {
                        // Add the build script dependency repositories.
                        if (!qualityServiceRepositoryAdded && TokenBuildScriptRepositories.IsMatch(line))
                        {
                            outputLines.Add(GetFormattedBuildScriptLine(QualityServiceMavenRepo));
                            qualityServiceRepositoryAdded = true;
                        }
                        // Add the build script dependencies.
                        else if (!qualityServiceDependencyClassPathAdded && TokenBuildScriptDependencies.IsMatch(line))
                        {
                            outputLines.Add(GetFormattedBuildScriptLine(QualityServiceDependencyClassPath));
                            qualityServiceDependencyClassPathAdded = true;
                        }
                    }
                }

                // Check if we need to add the plugin and add it.
                if (addPlugin)
                {
                    // Add the plugin.
                    if (!qualityServicePluginAdded && TokenApplicationPlugin.IsMatch(line))
                    {
                        outputLines.Add(QualityServiceApplyPlugin);
                        outputLines.AddRange(GenerateAppLovinPluginClosure(apiKey));
                        qualityServicePluginAdded = true;
                    }
                }
            }

            if ((addBuildScriptLines && (!qualityServiceRepositoryAdded || !qualityServiceDependencyClassPathAdded)) || (addPlugin && !qualityServicePluginAdded))
            {
                MaxSdkLogger.UserError("Failed to add AppLovin Quality Service plugin. Quality Service Plugin Added?: " + qualityServicePluginAdded + ", Quality Service Repo added?: " + qualityServiceRepositoryAdded + ", Quality Service dependency added?: " + qualityServiceDependencyClassPathAdded);
                return null;
            }
        }

        return outputLines;
    }

    private static string GetFormattedBuildScriptLine(string buildScriptLine)
    {
#if UNITY_2019_3_OR_NEWER
        return "            "
#else
        return "        "
#endif
               + buildScriptLine;
    }

    private static IEnumerable<string> GenerateAppLovinPluginClosure(string apiKey)
    {
        // applovin {
        //     // NOTE: DO NOT CHANGE - this is NOT your AppLovin MAX SDK key - this is a derived key.
        //     apiKey "456...a1b"
        // }
        var linesToInject = new List<string>(5);
        linesToInject.Add("");
        linesToInject.Add("applovin {");
        linesToInject.Add("    // NOTE: DO NOT CHANGE - this is NOT your AppLovin MAX SDK key - this is a derived key.");
        linesToInject.Add(string.Format(QualityServiceApiKey, apiKey));
        linesToInject.Add("}");

        return linesToInject;
    }
}

#endif
