# Duck Smash (Unity prototype)

Top-down bath-tub "smash" game: pull a rubber duck back like a slingshot, release, and knock through cookies,
crates, TNT, lasers and bosses. Ducks push each other like billiard balls; big combos turn a plain duck into a
one-use special duck (Bomb / Lightning / Rocket / Rainbow), unlocked gradually from level 12.

- Unity 2022.3 LTS, Built-in render pipeline
- Open `Assets/Scenes`, press Play — the whole game (map, 25 levels, UI, art, sound) is built at runtime by `Assets/Scripts/App.cs`
- All art is procedural 3D primitives + a custom toon shader (`Assets/Shaders/Toon.shader`); sounds are synthesized (`Sfx.cs`)
- Map has a "MÀN THỬ" (test level) button with every special duck unlocked
- Font: Noto Sans Display (SIL Open Font License)

Playable WebGL demo: https://dylananatole.github.io/demogame/
