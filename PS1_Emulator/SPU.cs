﻿using Microsoft.VisualBasic;
using NAudio.Dsp;
using NAudio.Wave;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Runtime.ConstrainedExecution;
using System.Text;
using System.Threading.Tasks;
using static PS1_Emulator.Voice;
using static PS1_Emulator.Voice.ADSR;

namespace PS1_Emulator {
    public class SPU {                            //Thanks to BlueStorm 
        const uint baseAddress = 0x1f801c00;
        public Range range = new Range(baseAddress, 640);
        byte[] RAM = new byte[512*1024];

        UInt16 SPUCNT;
        private bool reverbEnabled;

        //STAT:
        byte SPU_Mode;                          //0-5
        byte IRQ_Flag;                          //6
        public byte DMA_Read_Write_Request;     //7 seems to be same as SPUCNT.Bit5
        public byte DMA_Write_Request;          //8            
        public byte DMA_Read_Request;           //9            
        byte Data_transfer_busy;                //10 (1 = busy)           
        byte Writing_Capture_Buffers;           //11 Writing to First/Second half of Capture Buffers (0=First, 1=Second)

        Int16 mainVolumeLeft;
        Int16 mainVolumeRight;
        Int16 vLOUT;
        Int16 vROUT;

        uint KOFF;
        uint KON;

        uint PMON;

        uint NON;           //Noise mode enable
        uint EON;           //Echo 

        uint CD_INPUT_VOLUME;
        uint external_Audio_Input_Volume;

        UInt16 transfer_Control;
        uint transfer_address;
        uint currentAddress;
        uint reverbCurrentAddress;
        Voice[] voices;

        //Reverb registers
        ushort mBASE;   //(divided by 8)
        ushort dAPF1;   //Type: disp
        ushort dAPF2;   //Type: disp
        short vIIR;     //Type: volume
        short vCOMB1;   //Type: volume
        short vCOMB2;   //Type: volume
        short vCOMB3;   //Type: volume
        short vCOMB4;   //Type: volume
        short vWALL;    //Type: volume
        short vAPF1;    //Type: volume
        short vAPF2;    //Type: volume
        ushort mLSAME;  //Type: src/dst
        ushort mRSAME;  //Type: src/dst
        ushort mLCOMB1; //Type: src
        ushort mRCOMB1; //Type: src
        ushort mLCOMB2; //Type: src
        ushort mRCOMB2; //Type: src
        ushort dLSAME;  //Type: src
        ushort dRSAME;  //Type: src
        ushort mLDIFF;  //Type: src/dst
        ushort mRDIFF;  //Type: src/dst
        ushort mLCOMB3; //Type: src
        ushort mRCOMB3; //Type: src
        ushort mLCOMB4; //Type: src
        ushort mRCOMB4; //Type: src
        ushort dLDIFF;  //Type: src
        ushort dRDIFF;  //Type: src
        ushort mLAPF1;  //Type: src/dst
        ushort mRAPF1;  //Type: src/dst
        ushort mLAPF2;  //Type: src/dst
        ushort mRAPF2;  //Type: src/dst
        short vLIN;     //Type: volume
        short vRIN;     //Type: volume

        private WaveOutEvent waveOutEvent = new WaveOutEvent();
        private BufferedWaveProvider bufferedWaveProvider = new BufferedWaveProvider(new WaveFormat());
        //private WaveFileWriter waveFileWriter = new WaveFileWriter("output.wav", new WaveFormat());     //For audio dumping

        public SPU() {


           voices = new Voice[24];
           for (int i = 0; i < voices.Length; i++) { 
                voices[i] = new Voice();
            
           }

            bufferedWaveProvider.DiscardOnBufferOverflow = true;
            bufferedWaveProvider.BufferDuration = new TimeSpan(0, 0, 0, 0, 300);
            waveOutEvent.Init(bufferedWaveProvider);

        }
        public void store16(uint offset, UInt16 value) {

            switch (offset) {

                case uint when ((offset + baseAddress) >= 0x1F801C00 && (offset + baseAddress) <= 0x1F801D7F):        //Voice 0...23 

                    uint index = (((offset + baseAddress) & 0xFF0) >> 4) - 0xC0;         //index = offset/16 - 0xC0    (Inverse of the equation in psx-spx)
                    
                    switch ((offset + baseAddress) & 0xf) {

                        case 0x0: voices[index].volumeLeft = (short)value; break;
                        case 0x2: voices[index].volumeRight = (short)value; break;
                        case 0x4: voices[index].ADPCM_Pitch = value; break;
                        case 0x6: voices[index].ADPCM = value; break;
                        case 0x8: voices[index].setADSR_LOW(value); break;
                        case 0xA: voices[index].setADSR_HI(value); break;
                        case 0xC: voices[index].adsr.adsrVolume = value; break;
                        case 0xE: voices[index].ADPCM_RepeatAdress = value; break;
                        default: throw new Exception("unknown voice register: " + (offset & 0xf).ToString("x"));
                    
                    }
             
                    break;

                case 0x1aa: setCtrl(value); break;
                case 0x180: mainVolumeLeft = (short)value; break;
                case 0x182: mainVolumeRight = (short)value; break;
                case 0x184: vLOUT = (short)value; break;
                case 0x186: vROUT = (short)value; break;
                case 0x188: KON = KON & 0xFFFF0000 | value; break;
                case 0x18a: KON = KON & 0x0000FFFF | (uint)value << 16; break;
                case 0x18c: KOFF = KOFF & 0xFFFF0000 | value; break;
                case 0x18e: KOFF = KOFF & 0x0000FFFF | (uint)value << 16; break;
                case 0x190: PMON = PMON & 0xFFFF0000 | value; break;
                case 0x192: PMON = PMON & 0x0000FFFF | (uint)value << 16; break;
                case 0x194: NON = NON & 0xFFFF0000 | value; break;
                case 0x196: NON = NON & 0x0000FFFF | (uint)value << 16; break;
                case 0x198: EON = EON & 0xFFFF0000 | value; break;
                case 0x19a: EON = EON & 0x0000FFFF | (uint)value << 16; break;
                case 0x1b0: CD_INPUT_VOLUME = CD_INPUT_VOLUME & 0xFFFF0000 | value; break;
                case 0x1b2: CD_INPUT_VOLUME = CD_INPUT_VOLUME & 0x0000FFFF | (uint)value << 16; break;
                case 0x1b4: external_Audio_Input_Volume = external_Audio_Input_Volume & 0xFFFF0000 | value; break;
                case 0x1b6: external_Audio_Input_Volume = external_Audio_Input_Volume & 0x0000FFFF | (uint)value << 16; break;
                case 0x1ac: transfer_Control = value; break;

                case 0x1a6:
                    transfer_address = value;            //Store adress devided by 8
                    currentAddress = (uint)(value << 3); //Store adress multiplied by 8
                    break;

                case 0x1a8:
                    if((transfer_Control >> 1 & 7) != 2) { throw new Exception(); }
                    RAM[currentAddress] = (byte)(value & 0xFF);
                    RAM[currentAddress+1] = (byte)((value >> 8) & 0xFF);
                    currentAddress += 2;
                    break;

                case 0x19C:     //Read only?
                    for (int i = 0; i < 16;i++) {
                        voices[i].ENDX = (uint)((value >> i) & 1);
                    }
                    break;

                case 0x19E:
                    for (int i = 16; i < voices.Length; i++) {
                        voices[i].ENDX = (uint)((value >> i) & 1);
                    }
                    break;

                //Reverb area
                case 0x1a2: 
                    mBASE = value;
                    reverbCurrentAddress = (uint)value << 3; // <-- Can this cause problems if I don't have reverb?
                    break;

                case 0x1c0: dAPF1 = value; break;
                case 0x1c2: dAPF2 = value; break;
                case 0x1c4: vIIR = (short)value; break;
                case 0x1c6: vCOMB1 = (short)value; break;
                case 0x1c8: vCOMB2 = (short)value; break;
                case 0x1cA: vCOMB3 = (short)value; break;
                case 0x1cC: vCOMB4 = (short)value; break;
                case 0x1cE: vWALL = (short)value; break;
                case 0x1D0: vAPF1 = (short)value; break;
                case 0x1D2: vAPF2 = (short)value; break;
                case 0x1D4: mLSAME = value; break;
                case 0x1D6: mRSAME = value; break;
                case 0x1D8: mLCOMB1 = value; break;
                case 0x1DA: mRCOMB1 = value; break;
                case 0x1DC: mLCOMB2 = value; break;
                case 0x1DE: mRCOMB2 = value; break;
                case 0x1E0: dLSAME = value; break;
                case 0x1E2: dRSAME = value; break;
                case 0x1E4: mLDIFF = value; break;
                case 0x1E6: mRDIFF = value; break;
                case 0x1E8: mLCOMB3 = value; break;
                case 0x1EA: mRCOMB3 = value; break;
                case 0x1EC: mLCOMB4 = value; break;
                case 0x1EE: mRCOMB4 = value; break;
                case 0x1F0: dLDIFF = value; break;
                case 0x1F2: dRDIFF = value; break;
                case 0x1F4: mLAPF1 = value; break;
                case 0x1F6: mRAPF1 = value; break;
                case 0x1F8: mLAPF2 = value; break;
                case 0x1FA: mRAPF2 = value; break;
                case 0x1FC: vLIN = (short)value; break;
                case 0x1FE: vRIN = (short)value; break;

                default:
                    throw new NotImplementedException("Offset: " +offset.ToString("x")+ "\n" 
                                                      +"Full address: 0x" + (offset+0x1f801c00).ToString("x"));

            }




        }
    

        public UInt16 load16(uint offset) {

            switch (offset) {
                case 0x1aa:
                    return SPUCNT;
                case 0x1ae:
                    return readStat();

                case 0x180:
                    return (ushort)mainVolumeLeft;
                case 0x182:
                    return (ushort)mainVolumeRight;

                case 0x184:
                    return (ushort)vLOUT;

                case 0x186:
                    return (ushort)vROUT;

                case 0x188:

                    return (ushort)KON;

                case 0x18a:

                    return (ushort)(KON>>16);

                case 0x18c:

                    return (ushort)KOFF;

                case 0x18e:

                    return (ushort)(KOFF>>16);

                case 0x1ac:
                    return transfer_Control;

                case uint when ((offset + baseAddress) >= 0x1F801C00 && (offset + baseAddress) <= 0x1F801D7F):        //Voice 0...23 
                    uint index = (((offset + baseAddress) & 0xFF0) >> 4) - 0xC0;         //index = offset/16 - 0xC0    (Inverse of the equation in psx-spx)


                    switch ((offset + baseAddress) & 0xf) {

                        case 0x0:
                            
                            return (ushort)voices[index].volumeLeft;

                        case 0x2:
                            return (ushort)voices[index].volumeRight;

                        case 0x4: 

                            return voices[index].ADPCM_Pitch;

                        case 0x6:

                            return voices[index].ADPCM;

                        case 0x8:

                            return voices[index].adsr.adsrLOW;

                        case 0xA:

                            return voices[index].adsr.adsrHI;


                        case 0xC:

                            return voices[index].adsr.adsrVolume;

                        case 0xE:

                            return voices[index].ADPCM_RepeatAdress;

                        default:

                            throw new Exception("unknown voice register: " + (offset & 0xf).ToString("x"));

                    }

                // 1F801E00h..1F801E5Fh - Voice 0..23 Internal Registers

                case uint when ((offset + baseAddress) >= 0x1F801E00 && (offset + baseAddress) <= 0x1F801E5F):

                    //Console.WriteLine("[SPU] ignored read from " + (offset + baseAddress).ToString("x"));
                    return 0;

                //1F801D98h - Voice 0..23 Reverb mode aka Echo On (EON) (R/W)
                case 0x19a: 
                    return (ushort)(EON >> 16);
                case 0x198: 
                    return (ushort)EON;

                case 0x1b8:

                    return (ushort)mainVolumeLeft;  

                case 0x1ba:

                    return (ushort)mainVolumeRight;  

                case 0x1a6:

                    return (ushort)transfer_address;

                case 0x190:

                    return (ushort)PMON;

                case 0x192:

                    return (ushort)(PMON >> 16);

                case 0x194:

                    return (ushort)NON;

                case 0x196:

                    return (ushort)(NON >> 16);


                default:
                    throw new NotImplementedException("Offset: " + offset.ToString("x") + "\n"
                                                      + "Full address: 0x" + (offset + 0x1f801c00).ToString("x"));


            }

        }



        private ushort readStat() {
            ushort status = 0;

            status = (ushort)((status << 0) | SPU_Mode);
            status = (ushort)((status << 6) | IRQ_Flag);
            status = (ushort)((status << 7) | DMA_Read_Write_Request);
            status = (ushort)((status << 8) | DMA_Write_Request);
            status = (ushort)((status << 9) | DMA_Read_Request);
            status = (ushort)((status << 10) | Data_transfer_busy);
            status = (ushort)((status << 11) | Writing_Capture_Buffers);

            //12-15 are uknown/unused (seems to be usually zero)

            return status;

        }

        public void setCtrl(UInt16 value) {

            SPUCNT = value;

            SPU_Mode = (byte)(value & 0x3F);
            DMA_Read_Write_Request = (byte)((value >> 5) & 0x1);
            reverbEnabled = ((value >> 7) & 1) == 1;        //Only affects Reverb bufffer write, SPU can still read from reverb area


        }

        private int clk_counter = 0;
        public const uint CYCLES_PER_SAMPLE = 0x300;
        byte[] outputBuffer = new byte[2048];
        int outputBufferPtr = 0;
        int sumLeft;
        int sumRight;
        private bool reverbReady = true;
     
        public void SPU_Tick(int cycles) {        //SPU Clock
            
            clk_counter += cycles;
            if (clk_counter < CYCLES_PER_SAMPLE) { return; }
            reverbReady = !reverbReady; //For half the frequency
            clk_counter = 0;

            uint edgeKeyOn = KON;
            uint edgeKeyOff = KOFF;
            KON = 0;
            KOFF = 0;

            sumLeft = 0;
            sumRight = 0;
            int reverbLeft = 0;
            int reverbRight = 0;
            int reverbLeft_Input = 0;
            int reverbRight_Input = 0;

            for (int i = 0; i < voices.Length; i++) {

                if ((edgeKeyOn & (1 << i)) != 0) {
                    voices[i].keyOn();
                }

                if ((edgeKeyOff & (1 << i)) != 0) {
                    voices[i].keyOff();
                }

                if (voices[i].adsr.phase == Voice.ADSR.Phase.Off) {
                    voices[i].latest = 0;

                    continue;

                }

                short sample = 0;

                if ((NON & (1 << i)) == 0) {

                    if (!voices[i].isLoaded) {
                        voices[i].getSamples(ref RAM);
                        voices[i].decodeADPCM();
                        
                    }

                    sample = voices[i].interpolate();
                    modulatePitch(i);
                    voices[i].checkSamplesIndex();
                }
                else {
                    Console.WriteLine("[SPU] Noise generator !");   
                    voices[i].adsr.adsrVolume = 0;
                    voices[i].latest = 0;

                }

                sample = (short)((sample * voices[i].adsr.adsrVolume) >> 15);
                voices[i].adsr.ADSREnvelope();

                voices[i].latest = sample;

                sumLeft += (sample * voices[i].getVolumeLeft()) >> 15;
                sumRight += (sample * voices[i].getVolumeRight()) >> 15;

               
                 if (((EON >> i) & 1) == 1) {   //Adding samples from any channel with active reverb
                    reverbLeft_Input += (sample * voices[i].getVolumeLeft()) >> 15;    
                    reverbRight_Input += (sample * voices[i].getVolumeRight()) >> 15;
                 }
               
            }

            if (reverbReady) {            
                (reverbLeft, reverbRight) = processReverb(reverbLeft_Input, reverbRight_Input);

                sumLeft += reverbLeft;
                sumRight += reverbRight;
            }

            sumLeft = (Math.Clamp(sumLeft, -0x8000, 0x7FFE) * mainVolumeLeft) >> 15;
            sumRight = (Math.Clamp(sumRight, -0x8000, 0x7FFE) * mainVolumeRight) >> 15;

            if (((SPUCNT >> 14) & 0x1) == 0) {  //SPU Mute
                sumLeft = 0;
                sumRight = 0;
            }
           
            outputBuffer[outputBufferPtr++] = (byte)sumLeft;
            outputBuffer[outputBufferPtr++] = (byte)(sumLeft >> 8);
            outputBuffer[outputBufferPtr++] = (byte)sumRight;
            outputBuffer[outputBufferPtr++] = (byte)(sumRight >> 8);

      
            if (outputBufferPtr >= 2048) {
                 
                playAudio(outputBuffer);
                outputBufferPtr -= 2048;

            }


        }


        private (int, int) processReverb(int leftInput, int rightInput) {

            //Apply reverb formula
            //Seems like any Multiplication/Addition needs to be clamped to (-0x8000 , +0x7FFF), not just the last values written to memory


            //  ___Input from Mixer (Input volume multiplied with incoming data)_____________

            int Lin = (vLIN * leftInput) >> 15;
            int Rin = (vRIN * rightInput) >> 15;
            
            //  ____Same Side Reflection (left-to-left and right-to-right)___________________            
              short leftSideReflection = (short)Math.Clamp((((Lin + 
                  ((reverbMemoryRead((uint)dLSAME << 3) * vWALL) >> 15) - 
                  reverbMemoryRead(((uint)mLSAME << 3) - 2)) * vIIR) >> 15) + 
                  reverbMemoryRead(((uint)mLSAME << 3) - 2), -0x8000, +0x7FFF);

              short rightSideReflection = (short)Math.Clamp((((Rin +
                  ((reverbMemoryRead((uint)dRSAME << 3) * vWALL) >> 15) -
                  reverbMemoryRead(((uint)mRSAME << 3) - 2)) * vIIR) >> 15) +
                  reverbMemoryRead(((uint)mRSAME << 3) - 2), -0x8000, +0x7FFF);
            /*
            TODO, handle vIIR bug:
            vIIR works only in range -7FFFh..+7FFFh. When set to -8000h,        
            the multiplication by -8000h is still done correctly, but, 
            the final result (the value written to memory) gets negated
             */
            reverbMemoryWrite(leftSideReflection, (uint)mLSAME << 3);
            reverbMemoryWrite(rightSideReflection, (uint)mRSAME << 3);



            // ___Different Side Reflection (left-to-right and right-to-left)_______________
            short leftSideReflection_d = (short)Math.Clamp((((Lin +
                  ((reverbMemoryRead((uint)dRDIFF << 3) * vWALL) >> 15) -
                  reverbMemoryRead(((uint)mLDIFF << 3) - 2)) * vIIR) >> 15 ) +
                  reverbMemoryRead(((uint)mLDIFF << 3) - 2), -0x8000, +0x7FFF);


            short rightSideReflection_d = (short)Math.Clamp((((Rin +
                  ((reverbMemoryRead((uint)dLDIFF << 3) * vWALL) >> 15) -
                  reverbMemoryRead(((uint)mRDIFF << 3) - 2)) * vIIR) >> 15) +
                  reverbMemoryRead(((uint)mRDIFF << 3) - 2), -0x8000, +0x7FFF);


            reverbMemoryWrite(leftSideReflection_d, (uint)mLDIFF << 3);
            reverbMemoryWrite(rightSideReflection_d, (uint)mRDIFF << 3);



            //  ___Early Echo (Comb Filter, with input from buffer)__________________________ 

            short Lout = (short)Math.Clamp((
                (vCOMB1 * reverbMemoryRead((uint)mLCOMB1 << 3)) >> 15) +
                ((vCOMB2 * reverbMemoryRead((uint)mLCOMB2 << 3)) >> 15) +
                ((vCOMB3 * reverbMemoryRead((uint)mLCOMB3 << 3)) >> 15) +
                ((vCOMB4 * reverbMemoryRead((uint)mLCOMB4 << 3)) >> 15), -0x8000, +0x7FFF);


            short Rout = (short)Math.Clamp((
                (vCOMB1 * reverbMemoryRead((uint)mRCOMB1 << 3)) >> 15) +
                ((vCOMB2 * reverbMemoryRead((uint)mRCOMB2 << 3)) >> 15) +
                ((vCOMB3 * reverbMemoryRead((uint)mRCOMB3 << 3)) >> 15) +
                ((vCOMB4 * reverbMemoryRead((uint)mRCOMB4 << 3)) >> 15), -0x8000, +0x7FFF);
            

            //  ___Late Reverb APF1 (All Pass Filter 1, with input from COMB)________________

            Lout = (short)Math.Clamp(Lout - Math.Clamp(((vAPF1 * reverbMemoryRead(((uint)mLAPF1 << 3) - ((uint)dAPF1 << 3))) >> 15), -0x8000, +0x7FFF), -0x8000, +0x7FFF);
            Rout = (short)Math.Clamp(Rout - Math.Clamp(((vAPF1 * reverbMemoryRead(((uint)mRAPF1 << 3) - ((uint)dAPF1 << 3))) >> 15), -0x8000, +0x7FFF), -0x8000, +0x7FFF);

            reverbMemoryWrite(Lout, (uint)mLAPF1 << 3);
            reverbMemoryWrite(Rout, (uint)mRAPF1 << 3);

            Lout = (short)Math.Clamp(Math.Clamp(((Lout * vAPF1) >> 15),-0x8000,+0x7FFF) + reverbMemoryRead(((uint)mLAPF1 << 3) - ((uint)dAPF1 << 3)), -0x8000, +0x7FFF);
            Rout = (short)Math.Clamp(Math.Clamp(((Rout * vAPF1) >> 15),-0x8000, +0x7FFF) + reverbMemoryRead(((uint)mRAPF1 << 3) - ((uint)dAPF1 << 3)), -0x8000, +0x7FFF);


            //  ___Late Reverb APF2 (All Pass Filter 2, with input from APF1)________________
            Lout = (short)Math.Clamp(Lout - Math.Clamp(((vAPF2 * reverbMemoryRead(((uint)mLAPF2 << 3) - ((uint)dAPF2 << 3))) >> 15), -0x8000,+0x7FFF), -0x8000, +0x7FFF);
            Rout = (short)Math.Clamp(Rout - Math.Clamp(((vAPF2 * reverbMemoryRead(((uint)mRAPF2 << 3) - ((uint)dAPF2 << 3))) >> 15), -0x8000, +0x7FFF), -0x8000, +0x7FFF);

            reverbMemoryWrite(Lout, (uint)mLAPF2 << 3);
            reverbMemoryWrite(Rout, (uint)mRAPF2 << 3);

            Lout = (short)Math.Clamp(Math.Clamp(((Lout * vAPF2) >> 15), -0x8000,+0x7FFF) + reverbMemoryRead(((uint)mLAPF2 << 3) - ((uint)dAPF2 << 3)), -0x8000, +0x7FFF);
            Rout = (short)Math.Clamp(Math.Clamp(((Rout * vAPF2) >> 15), -0x8000,+0x7FFF) + reverbMemoryRead(((uint)mRAPF2 << 3) - ((uint)dAPF2 << 3)), -0x8000, +0x7FFF);

            //  ___Output to Mixer (Output volume multiplied with input from APF2)___________
            int leftOutput = Math.Clamp((Lout * vLOUT) >> 15, -0x8000, +0x7FFF);
            int rightOutput = Math.Clamp((Rout * vROUT) >> 15, -0x8000, +0x7FFF);

            //  ___Finally, before repeating the above steps_________________________________
            reverbCurrentAddress = Math.Max((uint)mBASE << 3, (reverbCurrentAddress + 2) & 0x7FFFE);

            // Wait one 22050Hz cycle, then repeat the above stuff

            return (leftOutput , rightOutput);

        }

        
        private void reverbMemoryWrite(short value, uint address) {
            if (!reverbEnabled) { return; }     

            address += reverbCurrentAddress;
            uint start = (uint)mBASE << 3;
            uint end = 0x7FFFF;      
            uint final = (start + ((address - start) % (end - start))) & 0x7FFFE; //Aliengment for even addresses only

            RAM[final] = (byte)value;
            RAM[final + 1] = (byte)(value >> 8);

        }
        private short reverbMemoryRead(uint address) {
            address += reverbCurrentAddress;
            uint start = (uint)mBASE << 3;
            uint end = 0x7FFFF; 
            uint final = (start + ((address - start) % (end - start))) & 0x7FFFE; //Aliengment for even addresses only

            return (short)(((uint)RAM[final + 1] << 8) | RAM[final]);

        }

        private void modulatePitch(int i) {
            
            int step =  voices[i].ADPCM_Pitch;   //Sign extended to 32-bits

            if (((PMON & (1 << i)) != 0) && i > 0) {
                int factor = voices[i - 1].latest;
                factor += 0x8000;
                step = step * factor;
                step = step >> 15;
                step = step & 0x0000FFFF;
            }

            if (step > 0x3FFF) { step = 0x4000; }

            voices[i].pitchCounter = (voices[i].pitchCounter + (ushort)step);

        }

        
        

        public void playAudio(byte[] samples) {


            bufferedWaveProvider.AddSamples(samples, 0, samples.Length);
            if (waveOutEvent.PlaybackState != PlaybackState.Playing) {
                waveOutEvent.Volume = 1; 
                waveOutEvent.Play();

            }
            
        }

        internal void DMAtoSPU(uint data) {
            if ((transfer_Control >> 1 & 7) != 2) { throw new Exception(); }

            RAM[currentAddress] = (byte)(data & 0xFF);
            RAM[currentAddress + 1] = (byte)((data >> 8) & 0xFF);
            RAM[currentAddress + 2] = (byte)((data >> 16) & 0xFF);
            RAM[currentAddress + 3] = (byte)((data >> 24) & 0xFF);
            currentAddress += 4;

        }
    }
}
