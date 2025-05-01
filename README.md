# Stream Deck Direct Brightness Controller

Simple console app to control the brightness of a stream deck's screen through the command line. 

No dependencies - built with .NET 4.8 Framework included in Windows. It doesn't continue to run, it exits itself once the command is run. By default won't show a console window when run with an argument.

# How to use:

### Either run it and enter the brightness when prompted to test it out, or run it through the command line with a number from 0-100 as an argument, such as:
`StreamDeckBrightnessController.exe 50` 

### Running it with a command line argument will not show a window, unless you add the `-debug` statement, such as:
`StreamDeckBrightnessController.exe 50 -debug`
