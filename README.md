# Bound by Light

**Bound by Light** is a cooperative, isometric action game built in **Unity 6** with online
multiplayer. Two twins, permanently linked by a **tether of light**, must fight through a
cathedral, solve light-based puzzles and progress together. Neither can make it alone.

**▶️ [Watch the trailer on YouTube](https://www.youtube.com/watch?v=vPxlNmdxjN8)**

> ⚠️ **Public showcase repository.** Some assets are intentionally excluded (see
> [Excluded assets](#excluded-assets)), so opening a fresh clone will show the characters as
> missing / pink meshes. The goal of this repo is to share the **project structure, gameplay
> code and design documentation**, not to redistribute the team's artwork.

## About the project

This game was developed for the **Online Game Design** course at the
**Università degli Studi di Milano**, by **Team Juggernaut**.
The project took part in **New Game Designer 2026**, a showcase/competition for student
game projects.

## Gameplay & technical highlights

- **2-player online shooter co-op**, built on **Unity Netcode for GameObjects (NGO)**, host + client.
- **Tether mechanic**: the two players are physically bound by a light link that drives
  movement, combat and puzzles.
- **Isometric, orthographic camera** (Hades-style), including a custom **see-through-walls
  shader** so characters stay visible when behind geometry.
- **Networked combat**: projectile weapons with ammo/reload, melee & ranged enemies, and a
  **Bishop mini-boss** with multiple attack patterns.
- **Room-based progression**: wave encounters, sealed/cleared rooms, checkpoints and a
  co-op **revive** system.
- **Light puzzles**: beams, prisms and receivers.
- **URP** rendering and **FMOD** audio.

## Repository structure

- `Assets/_Project/` - all first-party content: gameplay **scripts**, scenes, prefabs,
  materials and level art.
- `Documentation/` - design & technical documents (see below).
- Other folders under `Assets/` are third-party packs from the Unity Asset Store (free).

## Documentation

The `Documentation/` folder contains the official documents produced for the course:

- **GDD - Game Design Document** (`GDD_Bound_by_Light.pdf`): the full design of the game,
  vision statement, story and characters, core mechanics, gameplay systems, UI, audience
  and references.
- **TDD - Technical Design Document** (`TDD_Bound_by_Light.pdf`): the online/technical
  architecture, client/server structure, network synchronization, backend, production
  plan and workload estimation.
- **GCD - Game Concept Document** (`GCD_Bound_by_Light.pdf`): the initial concept,
  pitch, key features, core game loop and early concept art.

## Excluded assets

To respect the work of the team's 3D artist and the team's branding, the following are
**not** part of this public repository:

- Character 3D models and animations (players, enemies, boss).
- Team logo and main-menu title artwork.

Everything else, gameplay code, scenes, prefabs, systems and level content, is fully
available. Characters will simply appear as missing meshes when the project is opened
from a clone.

## Tech stack

Unity 6 (6000.x) · URP · Netcode for GameObjects · FMOD · C#

## License & credits
University project by Team Juggernaut (2026) - all rights reserved.
Third-party assets belong to their respective authors.
