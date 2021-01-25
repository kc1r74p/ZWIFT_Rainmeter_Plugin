# ZWIFT Rainmeter Plugin
![Build](https://github.com/kc1r74p/ZWIFT_Rainmeter_Plugin/workflows/Build/badge.svg)
![Release](https://github.com/kc1r74p/ZWIFT_Rainmeter_Plugin/workflows/Release/badge.svg)

A plugin for Rainmeter to display information about a ZWIFT account

#### Distance/month example rainmeter skin using Plugin
![Simple distance per month example](zwift_rainmeter_api.png)

#### How to use
1. Download release or build plugin yourself
2. Copy ZWIFT_RM_API.dll from extracted .zip to

```.
├── <Rainmeter_install_path>\Rainmeter
|   ├── Rainmeter.exe
│   ├── Plugins
│   │   ├── ZWIFT_RM_API.dll
│   │   ├── ...
│   ├── ...
```
 
 3. Copy Skin or create skin to Rainmeter skin folder
    - not part of release archive but part of this repo: [ZWIFT_RM_SKIN](https://github.com/kc1r74p/ZWIFT_Rainmeter_Plugin/tree/main/ZWIFT_RM_SKIN)
 Example path might then look like:
 
 ```.
├── < C:\Users\<USER>\Documents\Rainmeter\Skins
|   ├── SkinExample
│   ├── ZWIFT_RM_SKIN
│   │   ├── Bar.ini
│   │   ├── @Resources
│   │   │   ├── Variables
│   │   │   │   ├── settings.inc
│   ├── ...
```
 
4. Edit settings.inc and add your Rainmeter credentials
    - Todo: encrypt password on disk/use Windows credentials manager...
5. Restart Rainmeter Process and active Skin
    - This may lag at start due to the fetching of all activities
    - Todo: Think about async data fetching storing for fast display/reload...
 
6. (Optional) Adjust Bar.ini or create new skin based on provided values

## Contribute
There are currently some bad performance limits with the way the data is fetched for each meter, this should be refactored to async fetch data and store it so that reloads are fluid and non blocking.

As for possible values, the API can provide all information which is available in Zwift. 
Calories, Activity type (Running, Cycling), Profile info, Level...

Feel free to open PR´s or Issues and enhance this plugin! ❤
