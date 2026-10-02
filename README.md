# Industrial Equipment Monitoring and Control System
A C#/.NET WPF desktop application for monitoring and controlling simulated industrial equipment using EtherNet/IP/CIP, XML configuration, and asynchronous device communication.
The project simulates a welding unit and demonstrates industrial equipment monitoring, network communication, background processing, and fault handling without requiring physical hardware.

# Features

-> Monitor equipment temperature, speed, pressure, and operating state
-> Start, stop, reset, connect, and disconnect simulated equipment
-> EtherNet/IP explicit messaging with basic CIP requests and commands
-> XML-based equipment and connection configuration
-> Asynchronous/background device polling to keep the WPF UI responsive
-> Temperature warnings and simulated equipment fault handling
-> Event and connection logging
-> Connection failure and disconnection handling
-> Automated communication self-check

# Technologies

C# · .NET 9 · WPF/XAML · XML · TCP/IP · EtherNet/IP · CIP · Async/Await

# Architecture

WPF Desktop Application
        |
        v
Equipment Service
   |          |
   |          +--- XML Configuration
   |
   +--- EtherNet/IP / CIP Client
                |
                v
        Equipment Simulator

The WPF application provides the operator interface, while the equipment service handles communication and background monitoring.
A separate C# simulator acts as a welding unit, allowing the application to communicate with simulated industrial equipment without physical hardware.

# Configuration

Equipment settings are stored in config/equipment.xml:
<Equipment>
  <Name>Welding Unit 01</Name>
  <Address>127.0.0.1</Address>
  <Port>44818</Port>
  <TemperatureLimit>80</TemperatureLimit>
  <PollIntervalMs>1000</PollIntervalMs>
</Equipment>

This keeps the equipment name, network settings, warning threshold, and polling interval configurable instead of hardcoded.

# Running the Project

1. Start the simulator
dotnet run --project src/IndustrialEquipmentMonitoring.Simulator
The simulated equipment will listen on 127.0.0.1:44818.

2. Start the WPF application
Open another terminal:
dotnet run --project src/IndustrialEquipmentMonitoring.App

3. Test the system
-> Click Connect to establish a session.
-> Click Start to run the simulated equipment.
-> Monitor temperature, speed, pressure, and operating mode.
-> Use Stop to stop the equipment.
-> Allow the temperature to rise to test warning and fault behavior.
-> Use Reset to clear a simulated fault.
-> Click Disconnect to close the session.

# Communication
The application uses EtherNet/IP explicit messaging over TCP port 44818.
The basic communication flow is:
TCP Connection
     ↓
RegisterSession
     ↓
ListIdentity
     ↓
CIP Equipment Reads / Commands
     ↓
UnregisterSession

Equipment polling runs in the background using asynchronous socket operations so network communication does not block the WPF UI.

# Self-Check

An automated self-check is also available:
dotnet run --project src/IndustrialEquipmentMonitoring.Simulator -- --self-check
It tests connection, equipment reads, start/stop behavior, fault handling, reset, and connection failure handling.

# Project Purpose
This project was built to gain hands-on experience combining C#/.NET desktop development, networking, industrial communication, XML configuration, and asynchronous processing.
Note: This is a learning and simulation project, not a production industrial control system. It implements a limited subset of EtherNet/IP/CIP explicit messaging and does not control real industrial equipment.

# Author
Rayyan Azher Miswani
Bachelor of Computer Science — Dalhousie University

