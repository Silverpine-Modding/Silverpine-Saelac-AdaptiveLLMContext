# Adaptive LLM Context 1.0.0

Initial public release for Silverpine 1.7.3.

- One adjustable context maximum from 4,096 to 32,768 tokens, defaulting to 16,384.
- Formula-derived response headroom, pruning trigger, and prompt retention target.
- A nominal pruning batch capped at 4,096 tokens, while retaining the base game's
  summary and long-term memory generation. Complete-message boundaries and
  full-history clears can produce different batch sizes.
- Settings in the main-menu Modding Tools menu and the inventory Mods tab,
  with presets, a live budget preview, and a BepInEx configuration file.
- Updated context for built-in local models, API mode, and remote endpoints.
  A running local Kobold server requires a game restart after changing context.
- Normal NPC response generation remains capped at 512 tokens.
- Persistent patches and registrations follow Silverpine's bootstrap lifetime
  rules and remain active after the initial plugin host is destroyed.

At the default setting, pruning starts above 15,360 prompt tokens and targets
11,264 prompt tokens. These totals include character information, instructions,
and memories as well as conversation history.

## Installation

Requires Silverpine 1.7.3, BepInEx 5, and
[Modding Tools 1.9.3 or newer](https://github.com/Silverpine-Modding/Silverpine-Saelac-Modding-Tools/releases/latest).
Close the game and extract `AdaptiveLLMContext-1.0.0.zip` into `BepInEx/plugins/`.
Alternatively, install the standalone DLL in `BepInEx/plugins/AdaptiveLLMContext/`.
Keep only one plugin copy and preserve existing configuration. Modding Tools
must remain installed once in its own shared folder.

Larger local contexts can increase VRAM/RAM use and generation latency. API
contexts may increase billed input tokens. Use a context supported by your
model and backend; see README.md for configuration and memory considerations.

## Validation

Release build passed with zero warnings and errors. Plugin identity, version,
dependency metadata, and the compression transpiler targets were checked against
the installed Silverpine 1.7.3 assemblies. This release has not been validated
with a live in-game conversation or a GPU memory benchmark.

The release includes an installable ZIP, standalone DLL, and SHA-256 checksums.
No game assemblies, model weights, credentials, saves, or player configuration
are included. Created by Saelac and ChatGPT.
