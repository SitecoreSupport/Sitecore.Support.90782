using System;
using System.Collections.Generic;
using System.IO;
using System.Web;
using Sitecore.Configuration;
using Sitecore.Data.Items;
using Sitecore.Diagnostics;
using Sitecore.IO;
using Sitecore.Pipelines.Upload;
using Sitecore.Resources.Media;
using Sitecore.SecurityModel;
using Sitecore.Web;
using Sitecore.Zip;

namespace Sitecore.Support.Pipelines.Upload
{
  public class Save : UploadProcessor
  {
    public void Process(UploadArgs args)
    {
      Assert.ArgumentNotNull(args, "args");
      for (var i = 0; i < args.Files.Count; i++)
      {
        var file = args.Files[i];
        if (!string.IsNullOrEmpty(file.FileName))
          try
          {
            var flag = IsUnpack(args, file);
            if (args.FileOnly)
            {
              if (flag)
              {
                UnpackToFile(args, file);
              }
              else
              {
                var filename = UploadToFile(args, file);
                if (i == 0)
                  args.Properties["filename"] = FileHandle.GetFileHandle(filename);
              }
            }
            else
            {
              List<MediaUploadResult> list;
              var uploader = new MediaUploader
              {
                File = file,
                Unpack = flag,
                Folder = args.Folder,
                Versioned = args.Versioned,
                Language = args.Language,
                AlternateText = args.GetFileParameter(file.FileName, "alt"),
                Overwrite = args.Overwrite,
                FileBased = args.Destination == UploadDestination.File
              };
              using (new SecurityDisabler())
              {
                list = uploader.Upload();
              }
              Log.Audit(this, "Upload: {0}", file.FileName);

              foreach (var result in list)
              {
                if (Settings.Media.AutoSetAlt)
                {
                  using (new SecurityDisabler())
                  {
                    result.Item.Editing.BeginEdit();
                    result.Item["Alt"] = !string.IsNullOrEmpty(uploader.AlternateText) ? uploader.AlternateText : Path.GetFileNameWithoutExtension(file.FileName);
                    result.Item.Editing.EndEdit();
                  }
                }

                ProcessItem(args, result.Item, result.Path);
              }
            }
          }
          catch (Exception exception)
          {
            Log.Error("Could not save posted file: " + file.FileName, exception, this);
            throw;
          }
      }
    }

    private void ProcessItem(UploadArgs args, MediaItem mediaItem, string path)
    {
      Assert.ArgumentNotNull(args, "args");
      Assert.ArgumentNotNull(mediaItem, "mediaItem");
      Assert.ArgumentNotNull(path, "path");
      if (args.Destination == UploadDestination.Database)
        Log.Info("Media Item has been uploaded to database: " + path, this);
      else
        Log.Info("Media Item has been uploaded to file system: " + path, this);
      args.UploadedItems.Add(mediaItem.InnerItem);
    }

    private static void UnpackToFile(UploadArgs args, HttpPostedFile file)
    {
      Assert.ArgumentNotNull(args, "args");
      Assert.ArgumentNotNull(file, "file");
      var filename = FileUtil.MapPath(TempFolder.GetFilename("temp.zip"));
      file.SaveAs(filename);
      using (var reader = new ZipReader(filename))
      {
        foreach (var entry in reader.Entries)
        {
          var path = FileUtil.MakePath(args.Folder, entry.Name, '\\');
          if (entry.IsDirectory)
          {
            Directory.CreateDirectory(path);
          }
          else
          {
            if (!args.Overwrite)
              path = FileUtil.GetUniqueFilename(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            lock (FileUtil.GetFileLock(path))
            {
              FileUtil.CreateFile(path, entry.GetStream(), true);
            }
          }
        }
      }
    }

    private string UploadToFile(UploadArgs args, HttpPostedFile file)
    {
      Assert.ArgumentNotNull(args, "args");
      Assert.ArgumentNotNull(file, "file");
      var filePath = FileUtil.MakePath(args.Folder, Path.GetFileName(file.FileName), '\\');
      if (!args.Overwrite)
        filePath = FileUtil.GetUniqueFilename(filePath);
      file.SaveAs(filePath);
      Log.Info("File has been uploaded: " + filePath, this);
      return Assert.ResultNotNull(filePath);
    }
  }
}