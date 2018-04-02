﻿namespace Unosquare.PiGpio.Samples.Workbench
{
    using ManagedModel;
    using NativeEnums;
    using System.Threading;
    using Unosquare.Swan;

    internal class ButtonInterrupts : WorkbenchItemBase
    {
        private GpioPin Pin = null;
        private uint PreviousTick = 0;

        public ButtonInterrupts(bool isEnabled) : base(isEnabled) { }

        protected override void Setup()
        {
            Pin = Board.Pins[4];
            NativeMethods.IO.GpioSetMode((SystemGpio)Pin.PinNumber, PortMode.Input);
            Pin.PullMode = GpioPullMode.Off;
            Pin.Interrupts.EdgeDetection = NativeEnums.EdgeDetection.EitherEdge;
            Pin.Interrupts.TimeoutMilliseconds = 1000;
            PreviousTick = Board.Timing.TimestampTick;
            Pin.Interrupts.Start((pin, level, tick) => 
            {
                if (level == LevelChange.NoChange)
                {
                    $"Pin: {pin} | Level: {Pin.Value} | Tick: {tick} | Elapsed: {((tick - PreviousTick) / 1000d):0.000} ms.".Info(Name);
                }
                else
                {
                    $"Pin: {pin} | Level: {level} | Tick: {tick} | Elapsed: {((tick - PreviousTick) / 1000d):0.000} ms.".Info(Name);
                }
                
                PreviousTick = tick;
            });
        }

        protected override void DoBackgroundWork(CancellationToken ct)
        {
            while (ct.IsCancellationRequested == false)
            {
                Board.Timing.Sleep(50);
            }
        }

        protected override void Cleanup()
        {
            Pin.Interrupts.Stop();
        }
    }
}