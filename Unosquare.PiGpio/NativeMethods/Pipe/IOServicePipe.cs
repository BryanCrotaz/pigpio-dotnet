﻿namespace Unosquare.PiGpio.NativeMethods.Pipe
{
    using System;
    using System.Globalization;
    using System.IO;
    using Interfaces;
    using NativeEnums;
    using NativeTypes;

    /// <summary>
    /// Pipe strategy pattern.
    /// </summary>
    public class IOServicePipe : IIOService, IDisposable
    {
        private readonly PipeWriter _pipeWriter;
        private readonly PipeReader _pipeReader;
        private readonly PipeReader _errorReader;
        private readonly IDictionary<SystemGpio, PipeReader> _notificationPipes = new Dictionary<SystemGpio, PipeReader>();
        private readonly IDictionary<SystemGpio, bool> _notificationLastLevel = new Dictionary<SystemGpio, bool>();

        /// <summary>
        /// Initializes a new instance of the <see cref="IOServicePipe"/> class.
        /// </summary>
        public IOServicePipe()
        {
            _pipeWriter = new PipeWriter(Constants.CommandPipeName);
            PipeWriter = _pipeWriter.Writer;
            _pipeReader = new PipeReader(Constants.ResultPipeName);
            PipeReader = _pipeReader.Reader;
            _errorReader = new PipeReader(Constants.ErrorPipeName);
            ErrorReader = _errorReader.Reader;

            IsConnected = true;
        }

        /// <inheritdoc />
        public ControllerMode Mode => ControllerMode.Pipe;

        /// <inheritdoc />
        public bool IsConnected { get; }

        #region Pipes
        private StreamWriter PipeWriter { get; }
        private StreamReader PipeReader { get; }
        private StreamReader ErrorReader { get; }
        #endregion

        /// <inheritdoc />
        public ResultCode GpioSetMode(SystemGpio gpio, PigpioPinMode mode)
        {
            char code;
            switch (mode)
            {
                case PigpioPinMode.Input:
                    code = 'R';
                    break;
                case PigpioPinMode.Output:
                    code = 'W';
                    break;
                case PigpioPinMode.Alt0:
                    code = '0';
                    break;
                case PigpioPinMode.Alt1:
                    code = '1';
                    break;
                case PigpioPinMode.Alt2:
                    code = '2';
                    break;
                case PigpioPinMode.Alt3:
                    code = '3';
                    break;
                case PigpioPinMode.Alt4:
                    code = '4';
                    break;
                case PigpioPinMode.Alt5:
                    code = '5';
                    break;
                default: 
                    throw new ArgumentException("Unknown mode: " + mode);
            }

            return SendCommandWithResultCode($"m {(int)gpio} {code}");
        }

        /// <inheritdoc/>
        public PigpioPinMode GpioGetMode(SystemGpio gpio)
        {
            char modeCode = SendCommandWithResult($"mg {(int)gpio}").ToUpperInvariant()[0];
            switch (modeCode)
            {
                case 'R':
                    return PigpioPinMode.Input;
                case 'W':
                    return PigpioPinMode.Output;
                case '0':
                    return PigpioPinMode.Alt0;
                case '1':
                    return PigpioPinMode.Alt1;
                case '2':
                    return PigpioPinMode.Alt2;
                case '3':
                    return PigpioPinMode.Alt3;
                case '4':
                    return PigpioPinMode.Alt4;
                case '5':
                    return PigpioPinMode.Alt5;
                default:
                    throw new Exception("Unknown mode returned by pigpiod: " + modeCode);
            }

        }

        /// <inheritdoc />
        public ResultCode GpioSetPullUpDown(SystemGpio gpio, GpioPullMode pullMode)
        {
            char code;
            switch (pullMode)
            {
                case GpioPullMode.Off:
                    code = 'o';
                    break;
                case GpioPullMode.Down:
                    code = 'd';
                    break;
                case GpioPullMode.Up:
                    code = 'u';
                    break;
                default:
                    throw new ArgumentException("Unknown mode: " + pullMode);
            }

            return SendCommandWithResultCode($"pud {(int)gpio} {code}");
        }

        /// <inheritdoc />
        public bool GpioRead(SystemGpio gpio)
        {
            var result = SendCommandWithResult($"r {(int)gpio}");
            return result == "1";
        }

        /// <inheritdoc />
        public ResultCode GpioWrite(SystemGpio gpio, bool value)
        {
            return SendCommandWithResultCode($"w {(int)gpio} {(value ? 1 : 0)}");
        }

        /// <inheritdoc />
        public ResultCode GpioTrigger(UserGpio userGpio, uint pulseLength, bool value)
        {
            return SendCommandWithResultCode($"trig {(int)userGpio} {pulseLength} {(value ? 1 : 0)}");
        }

        /// <inheritdoc />
        public uint GpioReadBits00To31()
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public uint GpioReadBits32To53()
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public ResultCode GpioWriteBits00To31Clear(BitMask bits)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public ResultCode GpioWriteBits32To53Clear(BitMask bits)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public ResultCode GpioWriteBits00To31Set(BitMask bits)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public ResultCode GpioWriteBits32To53Set(BitMask bits)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public ResultCode GpioSetAlertFunc(UserGpio userGpio, PiGpioAlertDelegate callback)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public ResultCode GpioSetAlertFuncEx(UserGpio userGpio, PiGpioAlertExDelegate callback, UIntPtr userData)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public ResultCode GpioSetWatchdog(UserGpio userGpio, uint timeoutMilliseconds)
        {
            return SendCommandWithResultCode($"wdog {(int)userGpio} {timeoutMilliseconds}");
        }

        /// <inheritdoc />
        public ResultCode GpioSetIsrFunc(SystemGpio gpio, EdgeDetection edge, int timeout, PiGpioIsrDelegate callback)
        {
            var gpioMask = (uint)Math.Pow(2, (int)gpio);
            if (_notificationPipes.ContainsKey(gpio)) return ResultCode.Ok;

            // get a handle
            string handle = SendCommandWithResult($"no");

            // listen to the pipe
            var reader = new PipeNotificationReader($"/dev/pigpio{handle}");
            reader.OnNotification += (PigpioNotification[] notifications) =>
            {
                foreach (var notification in notifications)
                {
                    var level = (notification.Level & gpioMask) > 0;
                    var hasOldLevel = _notificationLastLevel.ContainsKey(gpio);
                    var oldLevel = hasOldLevel && _notificationLastLevel[gpio];
                    LevelChange change;
                    if (level && (!hasOldLevel || !oldLevel))
                    {
                        change = LevelChange.LowToHigh;
                    }
                    else if (!level && (!hasOldLevel || oldLevel))
                    {
                        change = LevelChange.HighToLow;
                    }
                    else
                    {
                        change = LevelChange.NoChange;
                    }

                    if (edge == EdgeDetection.EitherEdge
                        || (edge == EdgeDetection.FallingEdge && change == LevelChange.HighToLow)
                        || (edge == EdgeDetection.RisingEdge && change == LevelChange.LowToHigh))
                    {
                        callback(gpio, change, notification.Tick);
                    }

                    _notificationLastLevel[gpio] = level;
                }
            };
            _notificationPipes[gpio] = reader;

            // start notifications on the handle
            return SendCommandWithResultCode($"nb {handle} {Math.Pow(2, (int)gpio)}");

        }

        /// <inheritdoc />
        public ResultCode GpioSetIsrFuncEx(SystemGpio gpio, EdgeDetection edge, int timeout, PiGpioIsrExDelegate callback, UIntPtr userData)
        {
            var gpioMask = (uint)Math.Pow(2, (int)gpio);
            if (_notificationPipes.ContainsKey(gpio)) return ResultCode.Ok;

            // get a handle
            string handle = SendCommandWithResult($"no");

            // listen to the pipe
            var reader = new PipeNotificationReader($"/dev/pigpio{handle}");
            reader.OnNotification += notifications =>
            {
                foreach (var notification in notifications)
                {
                    var level = (notification.Level & gpioMask) > 0;
                    var hasOldLevel = _notificationLastLevel.ContainsKey(gpio);
                    var oldLevel = hasOldLevel && _notificationLastLevel[gpio];
                    LevelChange change;
                    if (level && (!hasOldLevel || !oldLevel))
                    {
                        change = LevelChange.LowToHigh;
                    }
                    else if (!level && (!hasOldLevel || oldLevel))
                    {
                        change = LevelChange.HighToLow;
                    }
                    else
                    {
                        change = LevelChange.NoChange;
                    }

                    callback(gpio, change, notification.Tick, userData);
                    _notificationLastLevel[gpio] = level;
                }
            };
            _notificationPipes[gpio] = reader;

            // start notifications on the handle
            return SendCommandWithResultCode($"nb {handle} {Math.Pow(2, (int)gpio)}");

        }

        /// <inheritdoc />
        public ResultCode GpioSetGetSamplesFunc(PiGpioGetSamplesDelegate callback, BitMask bits)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public ResultCode GpioSetGetSamplesFuncEx(PiGpioGetSamplesExDelegate callback, BitMask bits, UIntPtr userData)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public ResultCode GpioGlitchFilter(UserGpio userGpio, uint steadyMicroseconds)
        {
            return SendCommandWithResultCode($"fg {(int)userGpio} {steadyMicroseconds}");
        }

        /// <inheritdoc />
        public ResultCode GpioNoiseFilter(UserGpio userGpio, uint steadyMicroseconds, uint activeMicroseconds)
        {
            return SendCommandWithResultCode($"fn {(int)userGpio} {steadyMicroseconds} {activeMicroseconds}");
        }

        /// <inheritdoc />
        public GpioPadStrength GpioGetPad(GpioPadId pad)
        {
            var result = SendCommandWithResult($"pads {(int)pad}");
            return (GpioPadStrength)Convert.ToInt32(result, CultureInfo.InvariantCulture);
        }

        /// <inheritdoc />
        public ResultCode GpioSetPad(GpioPadId pad, GpioPadStrength padStrength)
        {
            return SendCommandWithResultCode($"pads {(int)pad} {padStrength}");
        }

        /// <inheritdoc />
        public int GpioReadUnmanaged(SystemGpio gpio)
        {
            return GpioRead(gpio) ? 1 : 0;
        }

        /// <inheritdoc />
        public ResultCode GpioWriteUnmanaged(SystemGpio gpio, DigitalValue value)
        {
            return GpioWrite(gpio, value.ToBoolean());
        }

        /// <inheritdoc />
        public int GpioGetPadUnmanaged(GpioPadId pad)
        {
            return (int)GpioGetPad(pad);
        }

        /// <inheritdoc />
        public int GpioGetModeUnmanaged(SystemGpio gpio)
        {
            return (int)GpioGetMode(gpio);
        }

        /// <inheritdoc />
        public ResultCode GpioTriggerUnmanaged(UserGpio userGpio, uint pulseLength, DigitalValue value)
        {
            return GpioTrigger(userGpio, pulseLength, value.ToBoolean());
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            foreach (var reader in _notificationPipes.Values)
            {
                reader.Dispose();
            }

            _notificationPipes.Clear();
            _pipeWriter.Dispose();
            _pipeReader.Dispose();
            _errorReader.Dispose();
        }

        private void SendCommand(string cmd)
        {
            PipeWriter.WriteLine(cmd);
        }

        private string SendCommandWithResult(string cmd)
        {
            PipeWriter.WriteLine(cmd);
            return PipeReader.ReadLine();
        }

        private ResultCode SendCommandWithResultCode(string cmd)
        {
            PipeWriter.WriteLine(cmd);
            if (PipeReader.EndOfStream)
            {
                return ResultCode.Ok;
            }

            var result = PipeReader.ReadLine();
            var code = Convert.ToInt32(result, CultureInfo.InvariantCulture);
            return (ResultCode)code;
        }
    }
}