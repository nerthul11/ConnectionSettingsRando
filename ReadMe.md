# ConnectionSettingsRando (CSR)

ConnectionSettingsRando was designed to randomize settings from Hollow Knight Randomizer connections.

The goal is to allow individual connections to expose their settings to CSR, which can then apply a shared randomization algorithm before generation begins.

## Current Status

CSR provides a shared randomization system for settings exposed by RandomizerMod connections.

Supported:
- Registering settings objects from external connections
- Boolean settings
- Numeric settings
- Enum settings
- Nested settings objects
- Fields and properties
- Individual connection opt-in/out
- Boolean enabling weighting odds
- `MenuRange` attributes
- `DynamicBound` attributes
- User-defined randomization rules
- Excluding individual settings from randomization
- Forcing boolean settings to `true` or `false`
- Custom randomization code

Not currently supported:
- Dependency-aware randomization between unrelated settings

## Architecture

CSR works by having connections register their settings objects.

A connection provides:
- A name
- A getter for its settings
- An override callback to apply randomized settings

Example:

```csharp
CSR.Register(
    AccessRandomizer.Instance.GetName(),
    () => AccessManager.Settings,
    s => SettingsRandomizer.CopyTo(s, AccessManager.Settings)
);
```

CSR does not need to know about individual connection settings classes. It only operates on the object it receives.

## Randomization Flow

The current lifecycle is:

```text
RandomizerMod starts generation
        |
        v
RandoController.OnBeginRun
        |
        v
CSR.RandomizeAll(rng)
        |
        v
Registered connections receive randomized settings
        |
        v
RandomizerCore generation begins
```

CSR uses the same RNG provided by RandomizerMod, ensuring:

```text
Same seed
+ Same connection settings
+ Same CSR version

= Same randomized result
```

## Nested Settings

Nested settings are supported automatically and represented by their full path when applying randomization rules.

For example:

```text
AccessRandomizer.CustomKeys.MapperKey
```

This allows individual nested settings to be targeted without affecting settings with the same name elsewhere.

## Attributes and Constraints

CSR supports `MenuRange` and `DynamicBound` attributes when determining valid randomization values.

### MenuRange

A `MenuRange` defines the static range from which a numeric setting can be randomized.

For example:

```csharp
[MenuRange(0, 10)]
public int Cost { get; set; }
```

CSR will only generate values within the specified range.

### DynamicBound

A `DynamicBound` allows one setting to define the upper or lower bound of another setting.

For example:

```csharp
[DynamicBound(nameof(MaximumCost), true)]
public float MinimumCost { get; set; }

[DynamicBound(nameof(MinimumCost), false)]
public float MaximumCost { get; set; }
```

CSR first randomizes the settings normally and then validates their dynamic constraints. If a constraint is violated, the affected members are randomized again until the resulting settings satisfy all applicable bounds.

This also works with nested settings objects.

### CSRIgnore

Any member annotated with an attribute named "CSRIgnoreAttribute" will be silently ignored by CSR. Connections providing their own randomization code may wish to handle these themselves.

CSR does have an empty template of the attribute available for use, but to avoid a hard dependency on CSR for such attributes, connections should define their own local copy instead.

## Randomization Rules

CSR supports a user-defined rules file for controlling individual settings.

The rules file allows settings to be:

- Excluded from randomization
- Included in the randomization (not required but useful for subsets of elements)
- Forced to `true`
- Forced to `false`

Rules are matched against the full setting path.

The basic format is:

```text
Exclude:
AccessRandomizer.SplitTram
BreakableWallRandomizer.MylaShop.Enabled

ForceTrue:
AccessRandomizer.CustomKeys.MapperKey

ForceFalse:
GodhomeRandomizer.Enabled
```

When first opening the game with the mod installed, a directory by the name of `ConnectionSettingsRando\Rules` will be generated on the Saves folder. Inside it, you can use any number of files which will be read on alphabetical order.

For easier toggling of the files, a Disabled folder is also included inside it. Files inside the Disabled folder (or anywhere else for that matter) will be ignored.

### Exclude

Settings listed under `Exclude` are left unchanged and are not randomized.

```text
Exclude:
AccessRandomizer.SplitTram
BreakableWallRandomizer.MylaShop.Enabled
```

The full path is used, so a setting named `Enabled` can be excluded for one connection without excluding every `Enabled` setting.

### Include

Settings listed under `Include` are part of the randomized pool, and can be used to override subsets of elements that would otherwise be excluded.

```text
Exclude:
*Group*

Include:
Breakable Wall Randomizer.GroupWalls
```

In this case, any setting that has Group on the name will be excluded from the randomization pool, with the sole exception of `Breakable Wall Randomizer`'s Group Walls setting.

### ForceTrue

Settings listed under `ForceTrue` are forced to `true` instead of being randomized.

This option applies only to boolean settings.

```text
ForceTrue:
AccessRandomizer.SplitTram
```

### ForceFalse

Settings listed under `ForceFalse` are forced to `false` instead of being randomized.

This option also applies only to boolean settings.

```text
ForceFalse:
GodhomeRandomizer.Enabled
```

If the same setting appears in multiple force rules, the rule processed last takes precedence. Using `ForceTrue` and `ForceFalse` for the same setting is naturally not recommended but will not cause a crash.

### Pattern Matching

Rules can use either normal setting paths or regular expressions.

Simple paths can be written without any special syntax:

```text
Exclude:
AccessRandomizer.Enabled
```

Regular expressions can be used when a rule should match multiple settings.

For example:

```text
Exclude:
*.DefineRefs
```

would exclude `DefineRefs` from every matching connection.

This makes it possible to use simple paths for common cases while still allowing more advanced users to create broad rules when necessary.

## Connection Integration

A connection should:

1. Define its normal settings object.
2. Register it with CSR during initialization.
3. Allow CSR to override settings before generation begins.

CSR handles the randomization, nested settings, constraints, and user-defined rules automatically.
