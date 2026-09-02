using System.Text.Json;
using CKToolkit.Core.Common;
using CKToolkit.Core.Lang;
using CKToolkit.I18n;

namespace CKToolkit.Cli;

public static partial class CliHost
{
    private static int HandleLangList(string? gameOverride, string? configOverride, bool isJson, TextWriter stdout, TextWriter stderr)
    {
        var config = ToolkitConfig.Load(configOverride);
        var packs = PackLoader.DiscoverAll();

        var packList = packs.Values.Select(p => new
        {
            id = p.Meta.Id,
            name = p.Meta.Name,
            nativeName = p.Meta.NativeName,
            version = p.Meta.Version,
            authors = p.Meta.Authors,
            gameLangFolder = p.Meta.GameLangFolder,
            gameLangKey = p.Meta.GameLangKey,
            templateLang = p.Meta.TemplateLang,
            fontFace = p.Meta.Font.Face,
            isBuiltIn = p.IsBuiltIn,
            sourcePath = p.SourcePath
        }).ToList();

        var data = new
        {
            currentPack = config.Lang.Pack,
            currentFontFace = config.Lang.FontFace,
            packs = packList
        };

        var warnings = new List<string>(config.MigrationsApplied);
        if (config.LoadError is not null) warnings.Insert(0, config.LoadError);

        if (isJson)
        {
            var envelope = new JsonEnvelope
            {
                Ok = true,
                Command = "lang list",
                Data = data,
                Warnings = warnings
            };
            stdout.WriteLine(JsonSerializer.Serialize(envelope, JsonEnvelopeOptions));
        }
        else
        {
            stdout.WriteLine("可用語言包清單 (Available Language Packs):");
            stdout.WriteLine($"目前設定 (Current Config): {config.Lang.Pack} (字型: {config.Lang.FontFace})\n");
            foreach (var p in packs.Values)
            {
                string tag = p.IsBuiltIn ? "[內建 / Built-in]" : "[外部 / External]";
                stdout.WriteLine($"  * {p.Meta.Id} - {p.Meta.NativeName} ({p.Meta.Name}) v{p.Meta.Version} {tag}");
                stdout.WriteLine($"      作者: {string.Join(", ", p.Meta.Authors)}");
                stdout.WriteLine($"      語系代號: {p.Meta.GameLangKey} -> {p.Meta.GameLangFolder}\\ (模板: {p.Meta.TemplateLang})");
                stdout.WriteLine($"      預設字型: {p.Meta.Font.Face}");
            }
            if (warnings.Count > 0)
            {
                stdout.WriteLine("\n警告 / Warnings:");
                foreach (string w in warnings) stdout.WriteLine($"  ! {w}");
            }
        }

        return ExitCodes.Success;
    }

    private static int HandleLangInstall(List<string> options, string? gameOverride, string? configOverride, bool isJson, TextWriter stdout, TextWriter stderr)
    {
        string? targetPackId = null;
        string? fontFace = null;

        for (int i = 0; i < options.Count; i++)
        {
            string opt = options[i];
            if (opt.Equals("--pack", StringComparison.OrdinalIgnoreCase) && i + 1 < options.Count)
            {
                targetPackId = options[++i];
            }
            else if (opt.Equals("--font", StringComparison.OrdinalIgnoreCase) && i + 1 < options.Count)
            {
                fontFace = options[++i];
            }
        }

        if (string.IsNullOrWhiteSpace(targetPackId))
        {
            string err = Strings.Get("Error_LangInstallMissingPack");
            return OutputError("lang install", err, ExitCodes.InvalidArgs, isJson, stdout, stderr);
        }

        var packs = PackLoader.DiscoverAll();
        if (!packs.TryGetValue(targetPackId, out var pack))
        {
            string err = Strings.Get("Error_LangPackNotFound", targetPackId);
            return OutputError("lang install", err, ExitCodes.InvalidArgs, isJson, stdout, stderr);
        }

        var config = ToolkitConfig.Load(configOverride);
        if (config.LoadError is not null)
            return RejectCorruptConfig("lang install", config, isJson, stdout, stderr);
        if (!string.IsNullOrWhiteSpace(gameOverride))
        {
            config.GameDir = gameOverride;
        }

        config.Lang.Pack = pack.Meta.Id;
        if (!string.IsNullOrWhiteSpace(fontFace))
        {
            config.Lang.FontFace = fontFace;
        }
        else if (!string.IsNullOrWhiteSpace(pack.Meta.Font.Face))
        {
            config.Lang.FontFace = pack.Meta.Font.Face;
        }

        config.Save(configOverride);

        var warnings = new List<string>(config.MigrationsApplied);
        if (config.LoadError is not null) warnings.Insert(0, config.LoadError);

        var data = new
        {
            pack = config.Lang.Pack,
            fontFace = config.Lang.FontFace,
            gameLangFolder = pack.Meta.GameLangFolder,
            gameLangKey = pack.Meta.GameLangKey
        };

        if (isJson)
        {
            var envelope = new JsonEnvelope
            {
                Ok = true,
                Command = "lang install",
                Data = data,
                Warnings = warnings
            };
            stdout.WriteLine(JsonSerializer.Serialize(envelope, JsonEnvelopeOptions));
        }
        else
        {
            stdout.WriteLine(Strings.Get("Lang_Install_Success", config.Lang.Pack));
            stdout.WriteLine($"  - 語言包 ID: {config.Lang.Pack}");
            stdout.WriteLine($"  - 字型: {config.Lang.FontFace}");
            if (warnings.Count > 0)
            {
                stdout.WriteLine("\n警告 / Warnings:");
                foreach (string w in warnings) stdout.WriteLine($"  ! {w}");
            }
        }

        return ExitCodes.Success;
    }

    private static int HandleLangUninstall(string? gameOverride, string? configOverride, bool isJson, TextWriter stdout, TextWriter stderr)
    {
        var config = ToolkitConfig.Load(configOverride);
        if (config.LoadError is not null)
            return RejectCorruptConfig("lang uninstall", config, isJson, stdout, stderr);
        if (!string.IsNullOrWhiteSpace(gameOverride))
        {
            config.GameDir = gameOverride;
        }

        config.Lang.Pack = string.Empty;
        config.Save(configOverride);

        var warnings = new List<string>(config.MigrationsApplied);
        if (config.LoadError is not null) warnings.Insert(0, config.LoadError);

        var data = new
        {
            pack = string.Empty
        };

        if (isJson)
        {
            var envelope = new JsonEnvelope
            {
                Ok = true,
                Command = "lang uninstall",
                Data = data,
                Warnings = warnings
            };
            stdout.WriteLine(JsonSerializer.Serialize(envelope, JsonEnvelopeOptions));
        }
        else
        {
            stdout.WriteLine(Strings.Get("Lang_Uninstall_Success"));
            if (warnings.Count > 0)
            {
                stdout.WriteLine("\n警告 / Warnings:");
                foreach (string w in warnings) stdout.WriteLine($"  ! {w}");
            }
        }

        return ExitCodes.Success;
    }

    private static int HandleLangExportTemplate(List<string> options, string? gameOverride, string? configOverride, bool isJson, TextWriter stdout, TextWriter stderr)
    {
        string? outDir = null;
        string templateLang = "ENGLISH";

        for (int i = 0; i < options.Count; i++)
        {
            string opt = options[i];
            if (opt.Equals("--out", StringComparison.OrdinalIgnoreCase) && i + 1 < options.Count)
            {
                outDir = options[++i];
            }
            else if (opt.Equals("--template", StringComparison.OrdinalIgnoreCase) && i + 1 < options.Count)
            {
                templateLang = options[++i];
            }
        }

        if (string.IsNullOrWhiteSpace(outDir))
        {
            string err = Strings.Get("Error_ExportTemplateMissingOut");
            return OutputError("lang export-template", err, ExitCodes.InvalidArgs, isJson, stdout, stderr);
        }

        var config = ToolkitConfig.Load(configOverride);
        string? gameDir = GamePaths.FindGameDir(gameOverride, config.GameDir);

        if (gameDir is null || !GamePaths.IsGameDir(gameDir))
        {
            string err = Strings.Get("Error_GameNotFound");
            return OutputError("lang export-template", err, ExitCodes.GameNotFound, isJson, stdout, stderr);
        }

        string localPakPath = GamePaths.GetLocalPakPath(gameDir);
        if (!File.Exists(localPakPath))
        {
            string err = Strings.Get("Error_GameNotFound");
            return OutputError("lang export-template", err, ExitCodes.GameNotFound, isJson, stdout, stderr);
        }

        HmmPak localPak;
        try
        {
            localPak = HmmPak.Load(localPakPath);
        }
        catch (Exception ex)
        {
            return OutputError("lang export-template", Strings.Get("Error_GeneralFailure", $"讀取 local.pak 失敗：{ex.Message}"), ExitCodes.GeneralFailure, isJson, stdout, stderr);
        }

        try
        {
            LangInstaller.ExportTemplate(localPak, templateLang, outDir, msg =>
            {
                if (!isJson) stdout.WriteLine(msg);
            });
        }
        catch (Exception ex)
        {
            return OutputError("lang export-template", Strings.Get("Error_GeneralFailure", $"匯出範本失敗：{ex.Message}"), ExitCodes.GeneralFailure, isJson, stdout, stderr);
        }

        var warnings = new List<string>(config.MigrationsApplied);
        if (config.LoadError is not null) warnings.Insert(0, config.LoadError);

        var data = new
        {
            outDir = Path.GetFullPath(outDir),
            templateLang
        };

        if (isJson)
        {
            var envelope = new JsonEnvelope
            {
                Ok = true,
                Command = "lang export-template",
                Data = data,
                Warnings = warnings
            };
            stdout.WriteLine(JsonSerializer.Serialize(envelope, JsonEnvelopeOptions));
        }
        else
        {
            stdout.WriteLine(Strings.Get("Lang_ExportTemplate_Success", Path.GetFullPath(outDir)));
            if (warnings.Count > 0)
            {
                stdout.WriteLine("\n警告 / Warnings:");
                foreach (string w in warnings) stdout.WriteLine($"  ! {w}");
            }
        }

        return ExitCodes.Success;
    }

    private static int HandleLangImport(List<string> options, string? configOverride, bool isJson, TextWriter stdout, TextWriter stderr)
    {
        string? srcDir = null;
        bool overwrite = false;

        for (int i = 0; i < options.Count; i++)
        {
            string opt = options[i];
            if ((opt.Equals("--src", StringComparison.OrdinalIgnoreCase) ||
                 opt.Equals("--from", StringComparison.OrdinalIgnoreCase)) && i + 1 < options.Count)
            {
                srcDir = options[++i];
            }
            else if (opt.Equals("--overwrite", StringComparison.OrdinalIgnoreCase))
            {
                overwrite = true;
            }
        }

        if (string.IsNullOrWhiteSpace(srcDir))
        {
            string err = Strings.Get("Error_LangImportSourceMissing");
            return OutputError("lang import", err, ExitCodes.InvalidArgs, isJson, stdout, stderr);
        }

        Func<string, string, bool>? overwritePrompt = overwrite ? ((_, _) => true) : null;
        Result<LanguagePack> result = LangPackService.ImportPack(srcDir, customTargetBaseDir: null, overwritePrompt: overwritePrompt);

        if (!result.Success || result.Value is null)
        {
            return OutputError("lang import", result.ErrorMessage ?? Strings.Get("Error_GeneralFailure", "匯入失敗"), result.ExitCode, isJson, stdout, stderr);
        }

        LanguagePack pack = result.Value;
        string targetDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "langpacks", pack.Meta.Id));

        var config = ToolkitConfig.Load(configOverride);
        var warnings = new List<string>(config.MigrationsApplied);
        if (config.LoadError is not null) warnings.Insert(0, config.LoadError);

        var data = new
        {
            packId = pack.Meta.Id,
            name = pack.Meta.Name,
            nativeName = pack.Meta.NativeName,
            version = pack.Meta.Version,
            targetDir
        };

        if (isJson)
        {
            var envelope = new JsonEnvelope
            {
                Ok = true,
                Command = "lang import",
                Data = data,
                Warnings = warnings
            };
            stdout.WriteLine(JsonSerializer.Serialize(envelope, JsonEnvelopeOptions));
        }
        else
        {
            stdout.WriteLine(Strings.Get("Lang_Import_Success", pack.Meta.Name, pack.Meta.Id, targetDir));
            if (warnings.Count > 0)
            {
                stdout.WriteLine("\n警告 / Warnings:");
                foreach (string w in warnings) stdout.WriteLine($"  ! {w}");
            }
        }

        return ExitCodes.Success;
    }
}
