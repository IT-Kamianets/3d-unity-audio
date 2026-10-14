# IT-Kamianets 3D Engine: Unity Audio

`3d-unity-audio` provides sound for the **IT-Kamianets 3D Engine** in Unity.

It covers positional 3D sound, sound events with variations, footsteps by surface, volume groups, and music.

### Logic

A game plays a **sound event by name**, not a file: `footstep.stone` picks one of several clips and varies pitch and volume, so repeated sounds never feel copy-pasted. Changing the sound of an event is a data change, not a code change.

Audio data is not a schema of its own. Sound files are assets loaded by id through `3d-unity-assets`, and sounds placed in the world (a torch, a waterfall) come with the scene through `3d-unity-scene`. When `3d-asset-schema` and `3d-scene-schema` gain their audio parts, this package reads them; until then, events and clips are set up in Unity.

Every sound plays through a volume group (master, music, effects, voice, interface), so a settings screen changes one value per group instead of touching every sound.

Positional sound is part of gameplay, not decoration: in a competitive game, players locate others by footsteps and spell casts, so direction and distance must be reliable.

The package knows nothing about game rules or networking. It plays what it is told; a game decides that a remote player's footstep is heard and where.

### Status

Version 0.1.

* **Sound events** (`SoundEvent`): a name, clips (Unity `AudioClip`s and/or audio asset ids loaded through `3d-unity-assets`), volume and pitch ranges, group, 2D or 3D with min/max distance, loop, max voices, min interval, priority. Defined in a `SoundBank` asset (Assets → Create → IT-Kamianets → Sound Bank) or in code with `Define`. A name with no event plays the audio asset with that id.
* **`EngineAudio.Default`**: `Play(name, position)`, `PlayAttached(name, transform)`, `Play2D(name)` from a pool of 32 voices; never the same clip twice in a row; per-event voice limit and the oldest/lowest-priority voice replaced when all are busy; `Preload`, `Stop`, `IsPlaying`, a `Played` event for debugging.
* **Volume groups**: master, effects, music, voice, interface, applied in code (no mixer needed) and saved per player through `EngineStorage` (`audio.volume.<group>`); playing sounds follow changes at once. Optional routing to the game's `AudioMixer` groups.
* **Music**: `PlayMusic(name, fade)` crossfades between two sources and loops; `StopMusic(fade)`.
* **`FootstepPlayer`** next to a `CharacterMotor`: `footstep.<surface>` every stride (longer when sprinting), quieter when crouching, `land.<surface>` after a fall, louder for harder landings; falls back to `footstep` / `land`. Works from movement, so other players' network copies make footsteps too. Surface from an `AudioSurface` component or a `surface:stone` scene tag.
* **`AudioSceneResolver`**: audio assets placed in a scene become looping 3D sources; every other asset goes on to `EngineAssets`.

Checked in Play mode with the example footstep asset: 10 rapid plays keep 4 voices (the event's limit) with varied pitch; halving the effects volume changes playing sounds at once and is saved; music fades in, loops and fades out; 21 m walked with the virtual gamepad gives 25 footsteps (0.75 m stride); a 6 m drop plays the landing.

```csharp
var audio = EngineAudio.Default;
audio.AddBank(playerSounds);
audio.Play("spell.fireball.cast", caster.position);
audio.PlayAttached("spell.fireball.fly", projectile.transform);
audio.Play2D("ui.click");
audio.SetVolume(SoundGroup.Music, 0.4f);   // saved for this player
audio.PlayMusic("music/round-start");
```

### Roadmap

* Occlusion behind walls
* Reverb zones
* Audio settings from 3d-asset-schema (loop, volume, range) when the schema gains them
* Sound events as data (JSON) for non-Unity tools
* Stable v1 audio API
