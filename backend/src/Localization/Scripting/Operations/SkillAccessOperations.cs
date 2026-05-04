using System.Text.Json;
using Localization.DbAccess;
using Localization.Resolution;

namespace Localization.Scripting.Operations;

public sealed class SkillAccessOperations : IScriptOperation
{
    private readonly DbAccessor _db;

    public SkillAccessOperations(DbAccessor db)
    {
        _db = db;
    }

    public IReadOnlyList<string> SupportedOperations { get; } = new[]
    {
        "CurrentSkillParameter", "CurrentSubSkill", "CurrentSkillLevel"
    };

    public bool Execute(
        string operationName,
        string[] args,
        ResolutionContext context,
        ScriptEnvironment env,
        ref string? returnValue)
    {
        string A(int i) => i < args.Length ? args[i] : "";

        return operationName switch
        {
            "CurrentSkillParameter" => ExecuteCurrentSkillParameter(A(0), A(1), context, env),
            "CurrentSubSkill" => ExecuteCurrentSubSkill(A(0), A(1), context, env),
            "CurrentSkillLevel" => ExecuteCurrentSkillLevel(A(0), context, env),
            _ => false
        };
    }

    private bool ExecuteCurrentSkillLevel(string target, ResolutionContext ctx, ScriptEnvironment env)
    {
        if (ctx.SkillLevel is null) return false;
        env.Set(target, (double)ctx.SkillLevel.Value);
        return true;
    }

    private bool ExecuteCurrentSkillParameter(string target, string path, ResolutionContext ctx, ScriptEnvironment env)
    {
        if (ctx.SkillId is null) return false;
        if (ctx.SkillLevel is null) return false;
        if (!_db.TryGetSkill(ctx.SkillId, out var skill)) return false;

        if (!skill.TryGetProperty("parametersPerLevel", out var paramsArray) ||
            paramsArray.ValueKind != JsonValueKind.Array)
            return false;

        int levelIndex = ctx.SkillLevel.Value - 1;
        if (levelIndex < 0 || levelIndex >= paramsArray.GetArrayLength())
            return false;

        var levelParams = GetArrayElement(paramsArray, levelIndex);
        if (levelParams is null) return false;

        if (!JsonPathReader.TryGet(levelParams.Value, path, out var el))
            return false;

        SetFromJsonElement(env, target, el);
        return true;
    }

    private bool ExecuteCurrentSubSkill(string target, string path, ResolutionContext ctx, ScriptEnvironment env)
    {
        if (ctx.SubSkillId is null) return false;
        if (!_db.TryGetSubSkill(ctx.SubSkillId, out var subSkill)) return false;
        if (!JsonPathReader.TryGet(subSkill, path, out var el)) return false;

        SetFromJsonElement(env, target, el);
        return true;
    }

    private static JsonElement? GetArrayElement(JsonElement array, int index)
    {
        int i = 0;
        foreach (var item in array.EnumerateArray())
        {
            if (i == index) return item;
            i++;
        }
        return null;
    }

    private static void SetFromJsonElement(ScriptEnvironment env, string target, JsonElement el)
    {
        el = UnwrapV(el);
        if (el.ValueKind == JsonValueKind.Number)
            env.Set(target, JsonPathReader.AsDouble(el) ?? 0);
        else
            env.Set(target, JsonPathReader.AsString(el) ?? "");
    }

    private static JsonElement UnwrapV(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("v", out var vEl))
            return vEl;
        return el;
    }
}
