using System;
using System.Collections.Generic;
using System.Text;
using Cosmos.Core.Memory.Old;
using Cosmos.HAL.BlockDevice.Registers;

namespace Cosmos.HAL.BlockDevice.Ports
{
    public class Satapi : StoragePort
    {
        public Debug.Kernel.Debugger mSATAPIDebugger = new Debug.Kernel.Debugger("HAL", "SATAPI");

        public PortRegisters mPortReg;

        public override PortType mPortType => PortType.SATAPI;
        public override string mPortName => "SATAPI";
        public override uint mPortNumber => mPortReg.mPortNumber;

        public SATAPI(PortRegisters aSATAPIPort)
        {

            // Check if it is really a SATAPI Port!
            if (aSATAPIPort.mPortType != PortType.SATAPI || (aSATAPIPort.CMD & (1U << 24)) == 0)
            {
                Console.ForegroundColor = ConsoleColor.DarkRed;
                Console.Write("\n[Error]");
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($" 0:{aSATAPIPort.mPortNumber} is not a SATAPI port!");
                return;
            }
            mSATAPIDebugger.Send("SATAPI Constructor");

            mPortReg = aSATAPIPort;

            mBlockSize = 2048;
        }

        public void SendSATAPICommand(ATACommands aCommand, uint aStart, uint aCount)
        {
            mPortReg.IS = unchecked((uint)-1);

            int xSlot = FindCMDSlot(mPortReg);
            if (xSlot == -1) return;

            HBACommandHeader xCMDHeader = new HBACommandHeader(mPortReg.CLB, (uint)xSlot);
            xCMDHeader.CFL = 5;
            xCMDHeader.ATAPI = 1;
            xCMDHeader.PRDTL = 0;
            xCMDHeader.Write = 0;
            xCMDHeader.ClearBusy = 1;

            xCMDHeader.CTBA = Heap.MemAlloc(256) + (uint)(256 * xSlot);

            HBACommandTable xCMDTable = new HBACommandTable(xCMDHeader.CTBA, (uint)xSlot);

            FISRegisterH2D xCMDFIS = new FISRegisterH2D(xCMDTable.CFIS)
            {
                FISType = (byte)FISType.FIS_Type_RegisterH2D,
                IsCommand = 1,
                Command = (byte)ATACommands.Packet,
                Device = 0
            };

            byte[] xATAPICMD = new byte[12];
            xATAPICMD[0] = (byte)aCommand;
            xATAPICMD[2] = (byte)((aStart >> 0x18) & 0xFF);
            xATAPICMD[3] = (byte)((aStart >> 0x10) & 0xFF);
            xATAPICMD[4] = (byte)((aStart >> 0x08) & 0xFF);
            xATAPICMD[5] = (byte)((aStart >> 0x00) & 0xFF);
            xATAPICMD[9] = (byte)(aCount);
            for (uint i = 0; i < xATAPICMD.Length; i++)
            new Core.MemoryBlock(xCMDTable.ACMD, 12).Bytes[i] = xATAPICMD[i];
            
            int xSpin = 0;
            do xSpin++; while ((mPortReg.TFD & 0x88) != 0 && xSpin < 1000000);

            if (xSpin == 1000000)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("\n[Error]: ");
                Console.Write("Port timed out!");
                Console.ResetColor();
                return;
            };

            mPortReg.CI = 1U;

            while(true)
            {
                if((mPortReg.CI & (1 << xSlot)) == 0)
                {
                    break;
                }
                if ((mPortReg.IS & (1 << 30)) != 0)
                {
                    Console.ForegroundColor = ConsoleColor.DarkRed;
                    Console.Write("\n[Fatal]: ");
                    Console.Write("Fatal error occurred while sending command!");
                    Console.ResetColor();
                    return;
                }
            }

            if ((mPortReg.IS & (1 << 30)) != 0)
            {
                Console.ForegroundColor = ConsoleColor.DarkRed;
                Console.Write("\n[Fatal]: ");
                Console.Write("Fatal error occurred while sending command!");
                Console.ResetColor();
                return;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("\n[Success]: ");
            Console.Write("Command has been sent successfully!");
            Console.ResetColor();

            return;
        }

        public int FindCMDSlot(PortRegisters aPort)
        {
            // If not set in SACT and CI, the slot is free
            var xSlots = (aPort.SACT | aPort.CI);

            for (int i = 1; i < 32; i++)
            {
                if ((xSlots & 1) == 0)
                    return i;
                xSlots >>= 1;
            }
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("\n[Error]: ");
            Console.Write("Cannot find a free command slot!");
            Console.ResetColor();
            return -1;
        }

        public override void ReadBlock(ulong aBlockNo, ulong aBlockCount, byte[] aData)
        {
            SendSATAPICommand(ATACommands.Read, (uint)aBlockNo, (uint)aBlockCount);
        }

        public override void WriteBlock(ulong aBlockNo, ulong aBlockCount, byte[] aData)
        {

        }
    }
}