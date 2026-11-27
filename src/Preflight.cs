using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace LoopDesk {
internal sealed class PreflightException : InvalidOperationException {
    internal readonly int StepIndex;
    internal PreflightException(int stepIndex,string message,Exception inner)
        : base(message,inner) {StepIndex=stepIndex;}
}

internal sealed class Preflight {
    internal readonly Dictionary<string,List<Dictionary<string,string>>> Rows
        =new Dictionary<string,List<Dictionary<string,string>>>(StringComparer.OrdinalIgnoreCase);

    internal static Preflight Check(Macro macro,int first,int last,CancellationToken cancel) {
        var plan=new Preflight();
        try {
            Storage.Validate(macro);
            int start=Math.Max(0,first);
            int count=(last<0?macro.Steps.Count:Math.Min(last+1,macro.Steps.Count))-start;
            var selected=macro.Steps.Skip(start).Take(count).ToList();
            CheckSteps(selected,start,cancel);
            if(!String.IsNullOrWhiteSpace(macro.Csv)) {
                var rows=Storage.ReadCsv(macro.Csv,cancel);
                plan.Rows.Add(macro.Csv,rows);
                // All rows have the same column names after CSV parsing.
                CheckText(selected,start,rows[0],cancel);
            }
        } catch(PreflightException) {throw;}
        catch(OperationCanceledException) {throw;}
        catch(Exception e) {throw new PreflightException(-1,e.Message,e);}
        return plan;
    }

    static void CheckSteps(List<Step> steps,int start,CancellationToken cancel) {
        long total=0;
        int firstOverLimit=-1;
        int limit=Player.ExecutionLimit(steps.Count);
        for(int i=0;i<steps.Count;i++) {
            cancel.ThrowIfCancellationRequested();
            var step=steps[i];
            if(step.Skip)continue;
            try {
                if(step.Removed)throw new InvalidDataException("제거된 동작입니다. 이 줄을 삭제하고 다시 실행하세요.");
                if(step.Kind=="Group")CheckSteps(step.Children,0,cancel);
            } catch(InvalidDataException e) {throw Problem(start+i,step,e);}
            catch(PreflightException e) {throw Problem(start+i,step,e);}
            total+=step.EffectiveRepeats;
            if(firstOverLimit<0&&total>limit)firstOverLimit=i;
        }
        if(firstOverLimit>=0)
            throw Problem(start+firstOverLimit,steps[firstOverLimit],new InvalidDataException(
                "한 회차에 "+total.ToString("N0")+"개 동작이 예정되어 있습니다. 실행 한도는 "+limit.ToString("N0")+"개입니다."));
    }

    static void CheckText(List<Step> steps,int start,Dictionary<string,string> row,CancellationToken cancel) {
        for(int i=0;i<steps.Count;i++) {
            cancel.ThrowIfCancellationRequested();
            var step=steps[i];
            if(step.Skip)continue;
            try {
                if(step.Kind=="Group")CheckText(step.Children,0,row,cancel);
                else Player.Expand(step.Value,row);
            } catch(InvalidDataException e) {throw Problem(start+i,step,e);}
            catch(PreflightException e) {throw Problem(start+i,step,e);}
        }
    }

    static PreflightException Problem(int index,Step step,Exception inner) {
        return new PreflightException(index,(index+1)+"번 "+step.Label+" · "+inner.Message,inner);
    }
}
}

