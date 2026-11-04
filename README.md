# Match the Object (Classwork 19)

VR interaction for Meta Quest 2 / Quest 3 made in Unity. The user changes one object until it looks like a target object and then confirms the answer.

**Team**
- Alvaro Romero - GitHub: Gudis-01
- Kenzo Matoo - GitHub: <Kenzo's GitHub username>

**Versions:** Unity 6000.5.4f1, XR Interaction Toolkit 3.7.0, OpenXR 1.19 (Meta Quest Support), XR Hands 1.10.

## How it works
In front of the player there is a table with two objects: the **Target** (left) and **PlayerObject** (right). A world space canvas with buttons sits between the table and the player. The buttons work with the controller ray and with hand tracking.

The user can change:
- Shape: cube, sphere, cylinder
- Color: red, green, blue
- Size: small, medium, large
- Rotation: 90 degrees steps around X, Y and Z
- Reset: goes back to a red, medium cube without rotation
- Confirm: checks the answer

## Implementation
Everything is in `Assets/Scripts/MatchObjectGame.cs`, attached to the GameManager object.

- Each object is stored as a small class (`Obj`) with its shape, color, size and rotation. The player and the target use the same class.
- Both objects have three children (cube, sphere, cylinder). Only the one that matches the current shape is active. The color is applied to its material and the size is a uniform scale (0.1, 0.2, 0.3).
- `Update()` redraws both objects, so every button only changes one value.
- The rotation button multiplies the current rotation by 90 degrees around a world axis. The target is created with random multiples of 90 degrees, so it can always be reached with the buttons.
- `NewTarget()` picks a random shape, color, size and rotation when the scene starts.
- `Confirm()` compares the four values. Shape, color and size must be the same. The rotation check is not strict: a sphere and a cube look the same after 90 degrees turns, so they are always accepted, and for the cylinder only the direction of its axis matters (up or down looks the same).
- If everything matches, the feedback text turns green and says it is correct. If not, it turns orange and only says that something does not match, without saying what.

Scene setup: XR Origin Hands rig from the XRI samples, XR Interaction Manager, an EventSystem with XR UI Input Module, and a world space canvas with the Tracked Device Graphic Raycaster.

Build fixes for the Quest (the build only showed a black screen): the Android application entry point was changed from GameActivity to Activity. We also fixed the buttons that were wired to the wrong values (Sphere, Green and Blue), assigned the feedback text in the GameManager and fixed the encoding of the "¡Correcto!" message.

## AI use declaration
AI level: 2 - Assisted Development.

Tool: Claude (Anthropic).

What we used it for:
- Planning the game logic (how to store the state, how to validate the rotation without being too strict).
- Writing and then simplifying `MatchObjectGame.cs`.
- Reviewing the project when the Quest build showed a black screen and fixing the scene and settings problems listed above.

What we did ourselves: the scene, the canvas and button layout, the Unity and XR setup, the repository and the commits, and checking the final code.
