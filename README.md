# Adaptive LLM Context

Adaptive LLM Context replaces Silverpine 1.7.3's fixed LLM context and
dialogue-pruning thresholds with one adjustable maximum and a formula-derived
budget. It keeps more recent roleplay dialogue verbatim while preserving the
base game's one-sentence episodic summaries and structured long-term memories.

[Download the latest release](https://github.com/Silverpine-Modding/Silverpine-Saelac-AdaptiveLLMContext/releases/latest).
Created by Saelac and ChatGPT.

## Requirements

- Silverpine 1.7.3
- BepInEx 5
- [Modding Tools](https://github.com/Silverpine-Modding/Silverpine-Saelac-Modding-Tools/releases/latest) 1.9.3 or newer

Close Silverpine and extract the release ZIP into `BepInEx/plugins/`; it contains
an `AdaptiveLLMContext` folder. Alternatively, install the standalone
`AdaptiveLLMContext.dll` under
`BepInEx/plugins/AdaptiveLLMContext/`. Keep the single shared
`ModdingTools.dll` in its own existing plugin folder; do not copy it beside this
plugin. Keep only one copy of this plugin, preserve your existing configuration,
and restart Silverpine.

## Configuration

Open **Adaptive LLM Context** from either the main-menu Modding Tools menu or
the inventory **Mods** tab. The same setting is also stored in:

```text
BepInEx/config/renegadex.silverpine.adaptivellmcontext.cfg
```

`MaximumContextTokens` defaults to `16384`, accepts `4096` through `32768`, and
is normalized to 256-token steps. OpenRouter and remote-endpoint changes apply
to new requests immediately. A running local Kobold server cannot resize its
KV/context cache, so local GPU changes take effect after restarting Silverpine.

## Derived budget

Only the maximum context is configurable. The plugin calculates the rest:

```text
response headroom = clamp(round256(max / 16), 512, 2048)
pruning trigger    = max - response headroom
summary batch      = clamp(round256(max / 4), 1536, 4096)
retention target   = max(2048, pruning trigger - summary batch)
advanced memories = max * 75 / 256 characters
```

At the default 16K setting this produces:

| Budget | Tokens/characters |
|---|---:|
| Maximum context | 16,384 tokens |
| Response headroom | 1,024 tokens |
| Pruning trigger | 15,360 tokens |
| Post-pruning prompt target | 11,264 tokens |
| Nominal summary batch | 4,096 tokens |
| Advanced-memory allowance | 4,800 characters |

The retention target covers the complete input prompt, including dialogue,
character information, instructions, and memories. It is not a dialogue-only
allowance. The summary batch is nominal: Silverpine removes complete dialogue
messages, and prompt overshoot, message boundaries, and its minimum-history
rules can change how much is removed. Explicit full-history clears still use
the base game's full-clear behavior.

Silverpine's normal generation maximum remains 512 tokens. The larger response
headroom at large contexts provides safety for special generation paths while
preventing prompt history from filling the complete model window.

## Behavior and compatibility

- All built-in local definitions, the API definition, debug Qwen, and loaded
  model definitions receive the selected effective maximum.
- New `GenParams` objects receive the same effective context.
- The local Kobold process starts with the selected context through the base
  game's existing `--contextsize` argument.
- The fixed 3,400-token post-pruning ceiling is replaced by the derived
  retention target.
- Silverpine's existing dialogue-summary and knowledge-extraction code remains
  responsible for creating memories.
- The plugin deliberately stops at 32K. Larger windows need broader prompt and
  hierarchical-memory changes and can impose very large local KV-cache costs.

## Lifetime behavior

Harmony patches and Modding Tools registrations are installed during plugin
`Awake` and intentionally remain installed for process lifetime. Silverpine
destroys the initial BepInEx plugin host during its bootstrap transition, so
the plugin does not unpatch Harmony or unregister static callbacks from that
host's `OnDestroy`. Temporary settings windows derive from `ModToolBehaviour`
and release their individual sessions normally when closed, disabled, or
destroyed.

## Memory and performance

Increasing local context can increase VRAM or system RAM use and prompt
processing time. GPU offload and backend settings determine where the extra
cache is stored. Silverpine enables sliding-window attention for its built-in
Gemma models, so total memory does not scale directly with the context setting.
Choose a lower maximum if the model no longer fits comfortably. API inference
keeps model memory on the provider, but longer prompts may increase cost and
latency. Remote servers must be configured to accept the selected context.

## Building

With the repository at `<Silverpine>/Plugin Development/AdaptiveLLMContext`,
run from this directory:

```powershell
dotnet build AdaptiveLLMContext.csproj -c Release
```

The release DLL is written to `bin/Release/AdaptiveLLMContext.dll`.
For a checkout elsewhere, provide your game installation directory:

```powershell
dotnet build AdaptiveLLMContext.csproj -c Release -p:GameDir="C:\Games\Silverpine"
```

Game, Unity, BepInEx, and Modding Tools assemblies are local build references
and are not bundled with this plugin. Release validation includes compilation
and inspection of the Harmony target patterns in Silverpine 1.7.3. In-game
dialogue quality and memory consumption have not been benchmarked for this
release.
