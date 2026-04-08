# ImmersCamResonite

This is a MOD for recording **VR180** video in Resonite. (It also includes 360-degree video support as a bonus.)

## Installation

1.  Install this MOD from **Thunderstore** using a Mod Manager.
2.  Install **BepisModSettings**.
3.  Launch Resonite via your Mod Manager.

## How to Use

This MOD outputs video via **Spout**. You can achieve VR180 recording by receiving the Spout signal in **OBS** and recording it there.
*This guide assumes OBS is already installed on your system.*

### OBS Configuration

1.  Install the **Spout2 plugin** for OBS. ([Link to GitHub](https://github.com/Off-World-Live/obs-spout2-plugin))
2.  Add a **Spout2 Capture** source to your scene.
3.  Set your **Canvas Resolution** to **6144x3072**. (While resolutions are adjustable, at least 6K/3K or higher is recommended for high-quality results.)
4.  Configure your **Output Settings** as follows:
      * **Video Encoder:** NVIDIA NVENC HEVC
      * **Rate Control:** CBR
      * **Bitrate:** 200,000 Kbps
      * **Preset:** P3 Fast
      * **Multi-pass Mode:** 1 Pass

### Resonite Configuration

1.  Copy and paste the following URL into Resonite and save the spawned folder to your inventory:
    `resrec:///U-orange/R-283474B975465A2BF6F0B3A29F9E46515262C40B601429CDE229D603954869C1`
2.  Open the folder in your inventory and spawn the **SkyCamera by orange V1.10 VR180 System**.
3.  Click the **blue circle** to enter the camera anchor. Use the left/right sticks and triggers to control the camera.
4.  You can adjust camera settings via the **Dash Menu** using **BepisModSettings**. This allows you to toggle the camera on/off, switch between 360/VR180, change resolution, and adjust the **IPD**.

### Shooting Tips

  * **Keep the camera steady:** Avoid moving the camera during recording. If you must move it, do so very slowly. Sudden camera movements in VR180 can cause severe motion sickness.
  * **Maintain a level horizon:** Unless you have a specific artistic reason, keep the camera’s roll and pitch horizontal. It is highly recommended to enable the **PitchLock** feature on the SkyCamera.
  * **Adjusting IPD:** You can control the sense of depth using the IPD setting. A wider IPD increases the 3D effect but makes the world look smaller. A narrower IPD reduces the depth effect and makes the world feel larger. Be careful: if the subject is too close to the camera at a high IPD, the parallax may become too intense, making the video uncomfortable to watch.

-----

### Credits

  * This MOD was ported to Bepis based on **kokoa's Reso360Spout** ([GitHub](https://www.google.com/search?q=https://github.com/rassi0429/Reso360Spout)).
  * The Spout implementation was referenced from **Zozokasu's ResoniteSpout** ([GitHub](https://www.google.com/search?q=https://github.com/Zozokasu/ResoniteSpout)).