using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LoopDesk {
public sealed class BackupAsset {
    public string Original {get;set;}
    public string Extension {get;set;}
    public byte[] Content {get;set;}
}
public sealed class BackupBundle {
    public Profile Profile {get;set;}
    public List<BackupAsset> Assets {get;set;}
    public int Version {get;set;}
    const long MaxBytes=48L*1024*1024;

    internal static IEnumerable<Step> Walk(Step step) {
        yield return step;
        if(step.Children!=null)
            foreach(var child in step.Children.SelectMany(Walk))yield return child;
    }

    internal static BackupBundle Create(Profile profile,bool includeCsv) {
        var paths=includeCsv?profile.Macros.Select(m=>m.Csv):Enumerable.Empty<string>();
        var assets=new List<BackupAsset>();
        long total=0;
        foreach(string path in paths.Where(p=>!String.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase)) {
            var file=new FileInfo(path);
            total+=file.Length;
            if(total>MaxBytes)throw new InvalidDataException("CSV 파일의 합계가 48MB를 넘습니다.");
            assets.Add(new BackupAsset {Original=path,Extension=file.Extension,Content=File.ReadAllBytes(path)});
        }
        return new BackupBundle {Profile=profile,Assets=assets,Version=1};
    }

    internal void Extract(string destination) {
        if(Version!=1||Assets==null||Profile==null||Profile.Macros==null)throw new InvalidDataException("지원하는 묶음 백업이 아닙니다.");
        if(Assets.Any(asset=>asset==null||String.IsNullOrWhiteSpace(asset.Original)||asset.Content==null)||
           Assets.Sum(asset=>(long)asset.Content.Length)>MaxBytes)
            throw new InvalidDataException("백업의 파일 목록을 확인하세요.");
        var paths=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var asset in Assets) {
            if(paths.ContainsKey(asset.Original))throw new InvalidDataException("백업에 중복 파일이 있습니다.");
            string extension=asset.Extension??"";
            if(extension.Length>12||extension.Length>0&&(extension[0]!='.'||extension.Skip(1).Any(c=>!Char.IsLetterOrDigit(c))))extension="";
            paths.Add(asset.Original,Path.Combine(destination,Guid.NewGuid().ToString("N")+extension));
        }
        if(Assets.Count>0)Directory.CreateDirectory(destination);
        foreach(var asset in Assets)File.WriteAllBytes(paths[asset.Original],asset.Content);
        foreach(var macro in Profile.Macros) {
            string replacement;
            if(macro.Csv!=null&&paths.TryGetValue(macro.Csv,out replacement))macro.Csv=replacement;
            foreach(var step in macro.Steps.SelectMany(Walk))
                if(step.Kind=="Image"&&step.Value!=null&&paths.TryGetValue(step.Value,out replacement))step.Value=replacement;
        }
    }
}
}

