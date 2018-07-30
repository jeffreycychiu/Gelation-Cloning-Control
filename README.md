# Gelation-Cloning-Control
This program allows for manual and automated control of a laser-microscope system integrated with a Laser and Camera system. Written for cell experiments using lasers

![Main Screen Picture](https://github.com/jeffreycychiu/Gelation-Cloning-Control/blob/master/Main%20Control%20resized.png)

## Capabilities
- Manual control of laser and stage
- Image processing - cell and colony detection
- Cell protein secretion measurement
- Well scanning and image stitching
- Auto-generate target points for laser
- Simple pulse width modulation (PWM) of laser


## Hardware involved:

Laser Module:   Arroyo 4308 LaserSource

Camera:         Basler pia2400-17gm

Microscope Stage and Controller: Prior H117 stage, ProScan III controller

Microscope:     Nikon Eclipse TI

*The program is specifically written for the laser, camera, and stage module above. Can be adapted to other systems but unfortunately I do not have the resources or time to implement and test a large amount of cameras and laser modules*

## Software and Libraries used:
EmguCV (OpenCV C# Wrapper)

FIJI ImageJ (used for grid stitching - OpenCV/EmguCV only had panorama stitching when this project was created. Also FIJI is created specifically for scientific imaging and is mnore configurable)

## Install notes:
Install Basler Pylon software & driver installed (otherwise will experience runtime SEHException error when running program): https://www.baslerweb.com/en/products/software/original-software/

Install FIJI ImageJ https://fiji.sc/



Written by Jeffrey Chiu
