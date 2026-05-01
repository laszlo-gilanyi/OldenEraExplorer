# Placeholder Resolution System

The system dynamically resolves placeholders (`{0}`, `{1}`, etc.) and script functions in game texts, substituting them with context-specific values.

## Table of Contents

1. [Architecture](#architecture)
2. [Resolution Context](#resolution-context)
3. [Resolution Process](#resolution-process)
4. [Script Operations](#script-operations)
5. [Alt-Text Resolution](#alt-text-resolution)
6. [Frontend Rendering](#frontend-rendering)
7. [Resolution Annotation](#resolution-annotation)
8. [Troubleshooting](#troubleshooting)

---

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    API Endpoints Layer                       │
│  (SpellsEndpoints, UnitsEndpoints, AbilitiesEndpoints...)   │
└───────────────────┬─────────────────────────────────────────┘
                    │ Resolve(sid, ctx, out trace)
                    ▼
┌─────────────────────────────────────────────────────────────┐
│              TextResolverFacade (ITextResolver)             │
│  • Selects Basic or Spec resolver                           │
│  • Based on PlaceholderResolverEnabled setting              │
└───────────────────┬─────────────────────────────────────────┘
                    │
        ┌───────────┴──────────┐
        ▼                      ▼
┌──────────────────┐  ┌───────────────────────────────────────┐
│  BasicResolver   │  │       PlaceholderResolver             │
│ • 3-tier lookup  │  │ • Template lookup                     │
│ • No placeholder │  │ • Args loading                        │
│   resolution     │  │ • Function evaluation                 │
│                  │  │ • Alt-text resolution                 │
│                  │  │ • Placeholder substitution            │
└──────────────────┘  └───────────┬───────────────────────────┘
                                  │
                      ┌───────────┴───────────┐
                      ▼                       ▼
            ┌──────────────────┐   ┌─────────────────────┐
            │ ScriptInterpreter│   │  FunctionOverrides  │
            │ • Script exec    │   │  • Manual overrides │
            │ • Operations     │   │  • Literal values   │
            │ • Formatting     │   └─────────────────────┘
            └────────┬─────────┘
                     │
                     ▼
            ┌─────────────────┐
            │   DbAccessor    │
            │ • JSON lookup   │
            │ • Core.zip data │
            └─────────────────┘
```

### Core Components

| Component | File | Purpose |
|-----------|------|---------|
| TextResolverFacade | `TextResolverFacade.cs` | Facade pattern, resolver selection |
| BasicResolver | `BasicResolver.cs` | Simple text lookup, no placeholders |
| PlaceholderResolver | `PlaceholderResolver.cs` | Full placeholder resolution |
| ScriptInterpreter | `ScriptInterpreter.cs` | `.script` file execution |
| LangIndex | `LangIndex.cs` | Localized text and args indexing |
| DbAccessor | `DbAccessor.cs` | JSON data reading from Core.zip |

---

## Resolution Context

The `ResolutionContext` record contains contextual information required for resolution:

```csharp
public sealed record ResolutionContext(string Locale)
{
    // Unit/Ability context
    public string? UnitId { get; init; }
    public int? AbilityIndex { get; init; }
    public bool? IsActiveAbility { get; init; }

    // Hero context
    public string? HeroSpecializationId { get; init; }
    public string? HeroAbilityId { get; init; }

    // Skill context
    public string? SkillId { get; init; }
    public string? SubSkillId { get; init; }
    public int? SkillLevel { get; init; }  // 1=Basic, 2=Advanced, 3=Expert

    // Buff/Debuff context
    public string? BuffId { get; init; }
    public int? BuffStacks { get; init; }
    public int? BuffSpellPower { get; init; }

    // Item/Artifact context
    public string? ItemId { get; init; }
    public int? ItemLevel { get; init; }
    public string? ItemSetId { get; init; }

    // Magic/Spell context
    public string? MagicId { get; init; }
    public int? MagicLevel { get; init; }

    // Building/Law context
    public string? FractionId { get; init; }
    public string? LawId { get; init; }
    public int? LawLevel { get; init; }  // 1-based law level (1, 2, 3, etc.)

    // Map object context
    public string? MapObjectId { get; init; }
}
```

### Context Usage

Each entity type requires its own context:

```csharp
// Artifact
var ctx = new ResolutionContext(locale) {
    ItemId = "tranquility_brightmind_tiara",
    ItemLevel = 3
};

// Buff
var ctx = new ResolutionContext(locale) {
    BuffId = "magic_haste_effect_0",
    BuffStacks = 1,
    BuffSpellPower = 50
};

// Unit Ability
var ctx = new ResolutionContext(locale) {
    UnitId = "academy_archmage",
    AbilityIndex = 0,
    IsActiveAbility = true
};
```

---

## Resolution Process

### 1. Text Lookup

```
Input:  sid = "artifact_brightmind_tiara_description"
Output: text = "Increases Mana Regeneration by {0}."
```

`LangIndex` loads the template from `Lang/{locale}/texts/*.json`, either from `StreamingAssets/Lang` on disk or from `Core.zip` when the game build embeds language data there.

**Directory Structure:**
```
StreamingAssets/Lang/             ← Demo-era on-disk layout
or Core.zip:Lang/                 ← Early Access/current layout
├── args/                          ← SHARED across all locales
│   ├── artifacts.json
│   ├── heroInfo.json
│   ├── magic.json
│   └── ...
├── english/
│   └── texts/
├── french/
│   └── texts/
└── ...
```

**Args format** (`Lang/args/*.json`, regardless of whether the source is disk or `Core.zip`):
```json
{
  "tokensArgs": [
    {"sid": "artifact_description_123", "args": ["current_item_param_1"]},
    {"sid": "buff_description_456", "args": ["current_buff_stacks", "current_buff_param_1"]}
  ]
}
```

**Texts format** (`Lang/{locale}/texts/*.json`):
```json
{
  "tokens": [
    {"sid": "artifact_description_123", "text": "Increases Mana Regeneration by {0}."},
    {"sid": "buff_description_456", "text": "Grants {0} stacks of {1} bonus."}
  ]
}
```

### 2. Args Loading

```
Input:  sid = "artifact_description_123"
Output: args = ["current_item_param_1"]
```

Args are **shared across all locales** - they are not locale-specific!

### 3. Placeholder Detection

```csharp
private static readonly Regex PlaceholderRx = new(@"\{(\d+)\}", RegexOptions.Compiled);
```

Extracts placeholder indices: `[0]` from `"... by {0}."`.

### 4. Function Evaluation (Eval)

The `Eval` method is the heart of the resolution:

```csharp
string Eval(string expr)
{
    // 1. Literal values (numbers, quoted strings)
    if (expr is numeric or "quoted") return literal;

    // 2. Alt-text with pipe syntax: "expr|alt_sid"
    if (expr contains '|') {
        var left = Eval(leftExpr);
        var altText = resolve(altSid);
        return Substitute(altText, map);
    }

    // 3. Manual overrides (FunctionOverrides)
    var ov = _overrides?.TryGet(expr);
    if (ov != null) return ov;

    // 4. Script evaluation (ScriptInterpreter)
    if (_interpreter?.TryEvaluate(expr, ctx, out var evalVal))
        return evalVal;

    // 5. Script constant (InfoScriptIndex)
    var lit = _script?.TryGetConstant(expr);
    if (lit != null) return lit;

    // 6. FALLBACK: Return marker
    return $"<{expr}>";
}
```

### 5. Placeholder Substitution

```csharp
private static string Substitute(string text, IDictionary<int, string> values, bool annotate)
{
    // Replace each placeholder with the appropriate value
    foreach (var kv in values)
        text = text.Replace("{" + kv.Key + "}", replacement);

    // Handle remaining placeholders
    text = PlaceholderRx.Replace(text, ...);

    return text;
}
```

### 6. Resolution Trace

The resolution returns a trace for debugging:

```csharp
public sealed record ResolutionTrace(
    string Sid,                          // Original SID
    IReadOnlyList<string> UsedSids,      // Alt-text SIDs used
    IReadOnlyList<string> UsedFunctions, // Functions attempted
    IReadOnlyList<string> Warnings,      // Non-fatal issues
    IReadOnlyList<string> Errors         // Fatal errors
);
```

---

## Script Operations

Functions defined in `.script` files perform various operations.

### CurrentItem

Reads artifact/item data from JSON by path.

```javascript
modInt current_item_param_1
{
    CurrentItem(level, "level")
    CurrentItem(baseData, "bonuses[0].parameters[1]")
    CurrentItem(upgData, "bonuses[0].upgrade.increment")

    Sub(upgCount, level, 1)
    Mul(upgValue, upgData, upgCount)
    Add(itemData, baseData, upgValue)

    Text(return, itemData)
}
```

**Special handling:**
- `"level"` path -> reads from `ctx.ItemLevel` (not JSON)
- `"config.X"` path -> the `"config."` prefix is stripped

### CurrentBuff

Reads buff data from JSON.

```javascript
modInt buff_haste_speed
{
    CurrentBuff(return, "data.stats.speed")
}
```

**Special handling:**
- `"config.X"` path -> prefix stripped
- `"dataConfig.X"` path -> prefix stripped

### CurrentBuffStacks / CurrentBuffSpellPower

Reads from ResolutionContext.

```javascript
modInt buff_damage_calculation
{
    CurrentBuffStacks(stacks, "")
    CurrentBuffSpellPower(sp, "")
    // ... calculation
}
```

### CurrentBuffSumMinDmg / CurrentBuffSumMaxDmg

Damage calculation with stacks and spell power.

```javascript
// Pseudocode
damage = baseDamage * stacks
if (spellPowerMult exists)
    damage = damage * (1 + spellPower * spellPowerMult / 100)
return damage
```

### Arithmetic Operations

| Operation | Description |
|-----------|-------------|
| `Add(result, a, b)` | result = a + b |
| `Sub(result, a, b)` | result = a - b |
| `Mul(result, a, b)` | result = a * b |
| `Div(result, a, b)` | result = a / b |
| `Text(return, value)` | Return value |

### "config" Prefix Handling

Scripts reference paths with `"config."` prefix, but the actual JSON has **no** such wrapper:

**Script:**
```javascript
CurrentItem(baseData, "config.bonuses[0].parameters[1]")
```

**Actual JSON:**
```json
{
  "id": "tranquility_brightmind_tiara",
  "bonuses": [
    {"type": "heroStat", "parameters": ["manaRestoreBonus", "8"]}
  ]
}
```

`ScriptInterpreter` automatically strips `"config."` and `"dataConfig."` prefixes.

---

## Alt-Text Resolution

The alt-text system provides alternative text when the main function returns no value.

### Pipe Syntax

```
"damage_calc|alt_text_damage_amount"
```

1. First attempts to evaluate the `damage_calc` function
2. If unsuccessful, loads the `alt_text_damage_amount` SID text
3. Alt-text can have its own args and placeholders

### Example

**Main template:**
```
Deals {0} damage.
```

**Args:**
```json
["damage_calc|alt_text_damage_amount"]
```

**Alt-text** (`alt_text_damage_amount`):
```
[ {0} + {1} × hero's Spell Power ]
```

**Alt-text args:**
```json
["base_damage", "spell_power_mult"]
```

**Result:**
```
Deals [ 20 + 5 × hero's Spell Power ] damage.
```

### Special Case: Percentage Indicator

If alt-text contains `%` character and a `<funcName>_add` function exists:

```csharp
if (altText.IndexOf('%') >= 0 && !leftExpr.EndsWith("_add"))
{
    var addExpr = leftExpr + "_add";
    var addVal = Eval(addExpr);
    if (!string.IsNullOrEmpty(addVal) && !addVal.StartsWith("<"))
        left = addVal;
}
```

---

## Frontend Rendering

### RichText Component

The frontend `RichText` component safely renders HTML tags with DOMPurify sanitization. Custom `<resolved>` and `<unresolved>` tags are first converted to styled `<span>` elements:

```tsx
import DOMPurify from 'dompurify';

/**
 * Processes resolution tags to include adjacent symbols (prefix/suffix) in the styled span.
 * Examples:
 * - `+<resolved>5</resolved>%` -> `<span class="resolved-value">+5%</span>`
 * - `–<resolved>3</resolved>% damage` -> `<span class="resolved-value">–3%</span> damage`
 */
function processResolutionTag(
  text: string,
  tagName: 'resolved' | 'unresolved',
  colorClass: string
): string {
  const regex = new RegExp(`([+\\-–]?)<${tagName}>(.*?)</${tagName}>(%)?`, 'gi');
  return text.replace(regex, (_match, prefix, content, suffix) => {
    const styledContent = `${prefix || ''}${content}${suffix || ''}`;
    return `<span class="${colorClass}">${styledContent}</span>`;
  });
}

export default function RichText({ text, className }: RichTextProps) {
  if (!text) return null;

  // Process custom resolution tags into styled spans
  let processedText = processResolutionTag(text, 'resolved', 'resolved-value');
  processedText = processResolutionTag(processedText, 'unresolved', 'unresolved-value');

  // Configure DOMPurify to allow spans with class attributes
  const clean = DOMPurify.sanitize(processedText, {
    ALLOWED_TAGS: ['b', 'i', 'span'],
    ALLOWED_ATTR: ['class'],   // Allow class attribute for styled spans
    KEEP_CONTENT: true,        // Keep text content even if tags are removed
  });

  return (
    <span
      className={cn(className)}
      dangerouslySetInnerHTML={{ __html: clean }}
    />
  );
}
```

### Supported HTML Tags

| Tag | Description | Source |
|-----|-------------|--------|
| `<b>` | Bold text | Game localization files |
| `<i>` | Italic text | Game localization files |
| `<resolved>` | Successfully resolved value (converted to `<span class="resolved-value">`) | Backend annotation |
| `<unresolved>` | Unresolved placeholder (converted to `<span class="unresolved-value">`) | Backend annotation |

### Security

DOMPurify automatically removes:
- `<script>` tags
- Event handlers (`onclick`, `onload`, etc.)
- `javascript:` URLs
- Dangerous attributes

---

## Resolution Annotation

The system provides visual feedback for resolved/unresolved values.

### How It Works

When **`ENABLE_ANNOTATION = true`**, the `Substitute` method marks values with markup tags:

| Type | Markup | Color |
|------|--------|-------|
| Successfully resolved | `<resolved>10</resolved>` | Orange (#FFA500) |
| Unresolved placeholder | `<unresolved>{0}</unresolved>` | Dark red (#8B0000) |
| Unresolved function | `<unresolved>&lt;funcName&gt;</unresolved>` | Dark red (#8B0000) |

### Example

**Input:**
```
Template: "Increases Attack by {0} and Defense by {1}."
Args: ["attack_bonus", "unknown_defense_function"]
```

**Output (without annotation):**
```
Increases Attack by 5 and Defense by <unknown_defense_function>.
```

**Output (with annotation):**
```html
Increases Attack by <resolved>5</resolved> and Defense by <unresolved>&lt;unknown_defense_function&gt;</unresolved>.
```

**Display:**
```
Increases Attack by 5 and Defense by <unknown_defense_function>.
                    ^                ^^^^^^^^^^^^^^^^^^^^^^^^^^^^
                 (orange)                  (dark red)
```

### CSS Styles

```css
/* Resolution annotation styling */
.resolved-value {
  color: #FFA500; /* Orange - successfully resolved values */
  font-weight: 700;
  font-size: 1.1em;
}

.unresolved-value {
  color: #8B0000; /* Dark red - unresolved placeholders/functions */
  font-weight: 700;
  font-size: 1.1em;
}

/* Light theme overrides for resolution annotation */
.theme-light .resolved-value {
  color: #CC7A00; /* Darker orange for light backgrounds */
}

.theme-light .unresolved-value {
  color: #CC0000; /* Darker red for light backgrounds */
}
```

### Enable/Disable

In `PlaceholderResolver.cs`:

```csharp
private const bool ENABLE_ANNOTATION = true;  // or false
```

---

## Troubleshooting

### `<funcName>` appears in the UI

**Cause:** Function not found in scripts or execution failed.

**Solutions:**
1. Check that the script function exists in `Core/DB/info/info_*/` files
2. Verify script syntax
3. Ensure required context fields are provided (ItemId, BuffId, etc.)

### `{0}` appears in the UI

**Cause:** Args not loaded or function returned empty/error value.

**Solutions:**
1. Check that `Lang/args/*.json` contains the SID
2. Verify the args array contains correct function names
3. Ensure functions execute successfully

### All artifact/buff placeholders fail

**Cause:** Missing context (ItemId/BuffId) or "config" prefix issue.

**Solutions:**
1. Verify per-entity context is created with appropriate ID
2. Check that CurrentItem/CurrentBuff strips the "config." prefix
3. Verify JSON structure matches expected paths

### Incorrect values displayed

**Cause:** Wrong JSON path, incorrect calculation, or missing context field.

**Solutions:**
1. Verify JSON paths in scripts match actual JSON structure
2. Check that context fields (ItemLevel, BuffStacks, etc.) are set correctly
3. Use ResolutionTrace to see which functions were called

---

## Cache System

`PlaceholderResolver` uses a memo cache for "plain" context resolutions (Texts tab):

```csharp
private readonly ConcurrentDictionary<string, (string text, ResolutionTrace trace)> _memo;
```

**Cacheable conditions:**
- No UnitId, AbilityIndex, IsActiveAbility
- No HeroSpecializationId, HeroAbilityId, SkillId, SubSkillId
- No BuffId, ItemId, ItemSetId, MagicId
- No FractionId, LawId, LawLevel, MapObjectId

If any context field is set, the resolution is not cached.

---

## 3-Tier Localization

Texts are loaded from a 3-level hierarchy:

1. **Overlay** (`OverlayService`) - UI labels with `oe_*` prefix
2. **Locale-specific** - `Lang/{locale}/texts/*.json`
3. **Fallback** - `Lang/english/texts/*.json` (if not found)

```csharp
// Tier 1: Overlay check
var overlayText = _overlays.TryResolveFromOverlay(sid, _lang.Locale);
if (!string.IsNullOrEmpty(overlayText))
    return overlayText;

// Tier 2-3: LangIndex (locale + fallback)
var text = _lang.ResolveText(sid);
```

---

## Performance Considerations

1. **Regex caching:** `PlaceholderRx` is compiled and static
2. **Memo cache:** Plain context resolutions are cached
3. **Lazy loading:** Scripts are loaded only when needed
4. **String builder:** StringBuilder used for large texts

---

## API Usage

### Simple Resolution

```csharp
var resolver = gameData.ResolverFacade;
var ctx = new ResolutionContext("english") { ItemId = "artifact_id", ItemLevel = 1 };
var result = resolver.Resolve("artifact_description_sid", ctx, out var trace);
```

### Trace Inspection

```csharp
if (trace.Errors.Count > 0)
{
    foreach (var error in trace.Errors)
        Console.WriteLine($"Error: {error}");
}

if (trace.Warnings.Count > 0)
{
    foreach (var warning in trace.Warnings)
        Console.WriteLine($"Warning: {warning}");
}
```

### Bulk Resolution

```csharp
foreach (var artifact in artifacts)
{
    var ctx = new ResolutionContext(locale) {
        ItemId = artifact.Id,
        ItemLevel = selectedLevel
    };

    var description = resolver.Resolve(artifact.DescSid, ctx, out _);
    // ...
}
```
