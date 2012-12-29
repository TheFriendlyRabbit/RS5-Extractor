﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace RS5_Extractor
{
    class Program
    {
        private static RS5Directory ProcessRS5File(Stream filestrm)
        {
            filestrm.Seek(0, SeekOrigin.Begin);
            byte[] fileheader = new byte[24];
            filestrm.Read(fileheader, 0, 24);
            string magic = Encoding.ASCII.GetString(fileheader, 0, 8);
            if (magic == "CFILEHDR")
            {
                long directory_offset = BitConverter.ToInt64(fileheader, 8);
                int dirent_length = BitConverter.ToInt32(fileheader, 16);
                return new RS5Directory(filestrm, directory_offset, dirent_length);
            }
            else
            {
                throw new InvalidDataException("File is not an RS5 file");
            }
        }

        private static Dictionary<string, List<AnimationClip>> ProcessEnvironmentAnimations(RS5Environment environ)
        {
            Dictionary<string, List<AnimationClip>> animclips = new Dictionary<string, List<AnimationClip>>(StringComparer.InvariantCultureIgnoreCase);

            #region animals
            foreach (KeyValuePair<string, dynamic> animal_kvp in environ.Data["animals"])
            {
                string key = animal_kvp.Key;
                dynamic animal = animal_kvp.Value;
                string modelset = "animals\\" + key;
                string modelname = animal["model"];
                if (modelset.ToLower().StartsWith(modelname.ToLower()))
                {
                    modelset = modelset.Substring(modelname.Length);
                    if (modelset.StartsWith("_"))
                    {
                        modelset = modelset.Substring(1);
                    }
                }
                modelset = Path.GetFileName(modelset);
                Dictionary<string, dynamic> animations = animal["animations"];
                foreach (KeyValuePair<string, dynamic> anim_kvp in animations)
                {
                    string animname = (modelset == "" ? "" : (modelset + ".")) + anim_kvp.Key;
                    string animparamstring = anim_kvp.Value;
                    Dictionary<string, string> animparams = animparamstring.Split(',').Select(v => v.Split(new char[] { '=' }, 2)).Where(v => v.Length == 2).ToDictionary(v => v[0], v => v[1]);
                    int startframe = Int32.Parse(animparams["F1"]);
                    int endframe = Int32.Parse(animparams["F2"]);
                    float framerate = animparams.ContainsKey("rate") ? Single.Parse(animparams["rate"]) : 60.0F;
                    if (!animclips.ContainsKey(modelname))
                    {
                        animclips[modelname] = new List<AnimationClip>();
                    }
                    animclips[modelname].Add(new AnimationClip { ModelName = modelname, Name = animname, StartFrame = startframe, NumFrames = endframe - startframe, FrameRate = framerate });
                }
            }
            #endregion

            #region creature movements
            foreach (KeyValuePair<string, dynamic> movement_kvp in environ.Data["creature"]["navigation"]["MOVEMENTS"])
            {
                string animname = movement_kvp.Key;
                string animparamstring = movement_kvp.Value;
                Dictionary<string, string> animparams = animparamstring.Split(',').Select(v => v.Split(new char[] { '=' }, 2)).Where(v => v.Length == 2).ToDictionary(v => v[0], v => v[1], StringComparer.InvariantCultureIgnoreCase);
                if (animparams.ContainsKey("F1") && animparams.ContainsKey("F2"))
                {
                    int startframe = Int32.Parse(animparams["F1"]);
                    int endframe = Int32.Parse(animparams["F2"]);
                    float framerate = animparams.ContainsKey("rate") ? Single.Parse(animparams["rate"]) : 60.0F;
                    foreach (string modelname in new string[]{ "creature", "creature_ik" })
                    {
                        if (!animclips.ContainsKey(modelname))
                        {
                            animclips[modelname] = new List<AnimationClip>();
                        }
                        animclips[modelname].Add(new AnimationClip { ModelName = modelname, Name = animname, StartFrame = startframe, NumFrames = endframe - startframe, FrameRate = framerate });
                    }
                }
            }
            #endregion

            #region hand animations
            foreach (KeyValuePair<string, dynamic> handanim_kvp in environ.Data["player"]["hand_animations"])
            {
                string animname = handanim_kvp.Key;
                Dictionary<string, dynamic> animparams = handanim_kvp.Value;
                if (animparams.ContainsKey("frames"))
                {
                    int startframe = animparams["frames"][0];
                    int endframe = animparams["frames"][1];
                    float framerate = 24.0F;
                    foreach (string modelname in new string[] { "hands", "hands_synth", "hands_ending" })
                    {
                        if (!animclips.ContainsKey(modelname))
                        {
                            animclips[modelname] = new List<AnimationClip>();
                        }
                        animclips[modelname].Add(new AnimationClip { ModelName = modelname, Name = animname, StartFrame = startframe, NumFrames = endframe - startframe, FrameRate = framerate });
                    }
                }
            }
            #endregion

            #region hand animations with game objects
            foreach (KeyValuePair<string, dynamic> obj_kvp in environ.Data["game_objects"])
            {
                string animname = obj_kvp.Key;
                Dictionary<string, dynamic> obj = obj_kvp.Value;
                if (obj.ContainsKey("SCRIPT"))
                {
                    Dictionary<string, dynamic> script = obj["SCRIPT"];
                    if (script.ContainsKey("frames"))
                    {
                        int startframe = script["frames"][0];
                        int endframe = script["frames"][1];

                        foreach (string modelname in new string[] { "hands", "hands_synth", "hands_ending" })
                        {

                            float framerate = 24.0F;
                            if (!animclips.ContainsKey(modelname))
                            {
                                animclips[modelname] = new List<AnimationClip>();
                            }
                            animclips[modelname].Add(new AnimationClip { ModelName = modelname, Name = animname, StartFrame = startframe, NumFrames = endframe - startframe, FrameRate = framerate });
                        }
                    }
                }
            }
            #endregion

            return animclips;
        }

        private static RS5Directory OpenRS5File(string filename)
        {
            string filepath = filename;
            if (!File.Exists(filepath))
            {
                string exepath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().CodeBase);
                filepath = exepath + Path.DirectorySeparatorChar + filename;

                if (!File.Exists(filepath))
                {

                    try
                    {
                        using (Microsoft.Win32.RegistryKey steamkey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Software\\Valve\\Steam"))
                        {
                            string steampath = steamkey.GetValue("SteamPath").ToString();
                            string miasmatadir = steampath + Path.DirectorySeparatorChar + "steamapps" + Path.DirectorySeparatorChar + "common" + Path.DirectorySeparatorChar + "Miasmata";
                            filepath = miasmatadir + Path.DirectorySeparatorChar + filename;

                            if (!File.Exists(filepath))
                            {
                                throw new FileNotFoundException();
                            }
                        }
                    }
                    catch
                    {
                        Console.WriteLine("Steam doesn't appear to be installed");
                        throw new FileNotFoundException();
                    }
                }
            }

            Console.WriteLine("Using {0} file from {1}", filename, filepath);
            Console.Write("Processing {0} central directory ... ", filename);
            RS5Directory rs5 = ProcessRS5File(File.Open(filepath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
            Console.WriteLine("Done");

            return rs5;
        }
        
        private static void WriteRS5Contents(RS5Directory main_rs5, Dictionary<string, List<AnimationClip>> animclips)
        {
            Console.WriteLine("Processing Textures ... ");
            foreach (KeyValuePair<string, RS5DirectoryEntry> dirent in main_rs5.Where(d => d.Value.Type == "IMAG"))
            {
                Texture.AddTexture(dirent.Value);
                Texture texture = Texture.GetTexture(dirent.Key);
                if (!texture.TextureFileExists())
                {
                    Console.Write("Saving texture {0}", dirent.Key);

                    texture.SaveImage();
                    Console.WriteLine("{0}  ({1} x {2})  {3}kiB", Path.GetExtension(texture.Filename), texture.Width, texture.Height, (new FileInfo(texture.Filename).Length + 1023) / 1024);
                    if (texture.Image.FailureReason != null)
                    {
                        Console.WriteLine("  Reason: {0}", texture.Image.FailureReason);
                    }

                    if (texture.Image.IntensityFactor != 1.0)
                    {
                        Console.WriteLine("  Intensity: {0}", texture.Image.IntensityFactor);
                    }

                    if (!texture.IsLossless && Path.GetExtension(texture.Filename) != ".dds")
                    {
                        Console.Write("Saving DDS texture {0}", dirent.Key);
                        texture.SaveDDS();
                        Console.WriteLine(".dds  ({0} x {1})  {2}kiB", texture.Width, texture.Height, (new FileInfo(texture.DDSFilename).Length + 1023) / 1024);
                        Console.WriteLine("  Reason: DDS couldn't be faithfully converted", texture.Image.FourCC);
                    }

                    texture.Flush();
                }
            }

            Console.WriteLine("Processing Immobile Models ... ");
            foreach (KeyValuePair<string, RS5DirectoryEntry> dirent in main_rs5.Where(d => d.Value.Type == "IMDL"))
            {
                Model model = new Model(dirent.Value);
                Collada.Exporter multimeshexporter = model.CreateMultimeshExporter(false);
                Collada.Exporter unanimatedexporter = model.CreateUnanimatedExporter(false);
                if (!File.Exists(multimeshexporter.Filename) || !File.Exists(unanimatedexporter.Filename))
                {
                    Console.WriteLine("Processing immobile model {0} ... ", dirent.Key);

                    if (!model.HasGeometry)
                    {
                        Console.WriteLine("    no geometry");
                    }
                    else
                    {
                        Console.WriteLine("    {0} textures, {1} vertices, {2} triangles", model.Textures.Count(), model.Vertices.Count(), model.Triangles.Count());

                        unanimatedexporter.Save(() => Console.Write("  Saving unanimated ... "), () => Console.WriteLine("Done"));
                        multimeshexporter.Save(() => Console.Write("  Saving multimesh ... "), () => Console.WriteLine("Done"));
                    }
                }
            }

            Console.WriteLine("Processing Animated Models ... ");
            foreach (KeyValuePair<string, RS5DirectoryEntry> dirent in main_rs5.Where(d => d.Value.Type == "AMDL"))
            {
                Model model = new Model(dirent.Value);
                List<Collada.Exporter> exporters = new List<Collada.Exporter>();
                List<Collada.Exporter<AnimationClip>> animexporters = new List<Collada.Exporter<AnimationClip>>();
                
                Collada.Exporter multimeshexporter = model.CreateMultimeshExporter(false);
                Collada.Exporter unanimatedexporter = model.CreateUnanimatedExporter(false);
                Collada.Exporter animatedexporter = model.CreateAnimatedExporter("ALL", false);

                exporters.Add(multimeshexporter);
                exporters.Add(unanimatedexporter);
                exporters.Add(animatedexporter);
                
                if (animclips.ContainsKey(model.Name))
                {
                    foreach (AnimationClip clip in animclips[model.Name])
                    {
                        Collada.Exporter<AnimationClip> exporter = model.CreateTrimmedAnimatedExporter(clip.Name, clip.StartFrame, clip.NumFrames, clip.FrameRate, false, clip);
                        exporters.Add(exporter);
                        animexporters.Add(exporter);
                    }
                }

                if (exporters.Select(e => File.Exists(e.Filename)).Any(v => !v))
                {
                    Console.WriteLine("Processing animated model {0} ... ", dirent.Key);

                    if (!model.HasGeometry)
                    {
                        Console.WriteLine("    no geometry");
                    }
                    else
                    {
                        Console.WriteLine("    {0} textures, {1} vertices, {2} triangles, {3} joints, {4} frames", model.Textures.Count(), model.Vertices.Count(), model.Triangles.Count(), model.Joints.Count(), model.NumAnimationFrames);

                        unanimatedexporter.Save(() => Console.Write("  Saving unanimated ... "), () => Console.WriteLine("Done"));
                        multimeshexporter.Save(() => Console.Write("  Saving multimesh ... "), () => Console.WriteLine("Done"));
                        animatedexporter.Save(() => Console.Write("  Saving all animations ({0} frames @ 24 fps) ... ", model.NumAnimationFrames), () => Console.WriteLine("Done"));

                        if (animclips.ContainsKey(model.Name))
                        {
                            foreach (Collada.Exporter<AnimationClip> exporter in animexporters)
                            {
                                exporter.Save((clip) => Console.Write("  Saving animation {0} ({1} frames @ {2} fps) ... ", clip.Name, clip.NumFrames, clip.FrameRate), (clip) => Console.WriteLine("Done"));
                            }
                        }
                        else
                        {
                            Console.WriteLine("  model clip start and end frames not in environment");
                        }
                    }
                }
            }
        }

        private static void Main(string[] args)
        {
            try
            {
                RS5Directory main_rs5 = OpenRS5File("main.rs5");
                RS5Directory environ_rs5 = OpenRS5File("environment.rs5");
                Console.Write("Processing environment ... ");
                RS5Environment environ = new RS5Environment(environ_rs5["environment"].GetData().Chunks["DATA"].Data);
                Dictionary<string, List<AnimationClip>> animclips = ProcessEnvironmentAnimations(environ);
                Console.WriteLine("Done");
                XElement environ_xml = environ.ToXML();

                try
                {
                    environ_xml.Save("environment.xml");
                    WriteRS5Contents(main_rs5, animclips);
                }
                catch (UnauthorizedAccessException)
                {
                    System.Reflection.Assembly asm = System.Reflection.Assembly.GetExecutingAssembly();
                    string exepath = Path.GetDirectoryName(asm.Location);
                    Console.WriteLine("Unable to write to working directory {0}", Environment.CurrentDirectory);
                    Console.WriteLine("Do you want to write to the directory the executable is in?");
                    Console.WriteLine("({0})", exepath);
                    Console.Write("[y/N] ");
                    ConsoleKeyInfo keyinfo = Console.ReadKey(false);
                    Console.WriteLine();
                    if (keyinfo.KeyChar == 'y' || keyinfo.KeyChar == 'Y')
                    {
                        Environment.CurrentDirectory = exepath;
                        environ_xml.Save("environment.xml");
                        WriteRS5Contents(main_rs5, animclips);
                    }
                }
            }
            catch (FileNotFoundException)
            {
                Console.WriteLine("Unable to load RS5 files");
            }
            /* catch (Exception ex)
            {
                Console.Error.WriteLine("Caught exception: {0}\n\nPlease report this to Ben Peddell <klightspeed@killerwolves.net>", ex.ToString());
            } */
            

            Console.Error.Write("Press any key to exit");
            Console.ReadKey(true);
        }
    }
}
