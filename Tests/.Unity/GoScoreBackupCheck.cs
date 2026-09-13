#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

public static class GoScoreBackupCheck
{
    private static int failures;

    private static void Expect(bool condition,string message)
    {
        if(condition)return;
        failures++;
        Debug.LogError("PURE_UDON_GO_SCORE_BACKUP_FAIL "+message);
    }

    private static float[] MakePolicy()
    {
        var values=new float[PureUdonGo.GoMctsSearch.POLICY_SIZE];
        for(int i=0;i<PureUdonGo.GoGame.AREA;i++)values[i]=-8f;
        values[0]=4f;
        return values;
    }

    private static bool Submit(PureUdonGo.GoMctsSearch search,float[] policy,float scoreMean,float scoreStdev,float noResultLogit)
    {
        return search.SubmitNeuralLogitsWithScore(search.searchToken,search.rootRevision,search.rootSettingsRevision,
            search.rootOwnerLifecycle,search.pendingHash0,search.pendingHash1,search.pendingHash2,search.pendingHash3,
            policy,-8f,0f,0f,noResultLogit,scoreMean,scoreStdev);
    }

    public static void Run()
    {
        failures=0;
        GameObject gameObject=null,stateObject=null,positiveObject=null,negativeObject=null,mixedObject=null;
        try
        {
            gameObject=new GameObject("GoScoreBackupGame");
            var game=gameObject.AddComponent<PureUdonGo.GoGame>();
            game.Start();
            stateObject=new GameObject("GoScoreBackupSimulationState");
            var state=stateObject.AddComponent<PureUdonGo.GoSearchState>();
            positiveObject=new GameObject("GoScoreBackupPositive");
            var positive=positiveObject.AddComponent<PureUdonGo.GoMctsSearch>();
            positive.simulationState=state;
            float[] policy=MakePolicy();
            Expect(positive.BeginSearch(game,1,31),"positive score search begins");
            Expect(Submit(positive,policy,19f,5f,-20f),"positive score result accepted");
            Expect(positive.phase==PureUdonGo.GoMctsSearch.PHASE_COMPLETE,"positive score search completes");
            Expect(Mathf.Abs(positive.rootNeuralScoreMean-19f)<0.001f,"root score mean is retained after no-result postprocess");
            Expect(Mathf.Abs(positive.rootNeuralScoreStdev-5f)<0.001f,"root score stdev is retained after no-result postprocess");
            Expect(positive.rootNeuralOutputRevision==positive.rootRevision,"root score revision is retained");
            float positiveValue=positive.GetRootMeanValue();
            Expect(positiveValue>0.05f,"positive score contributes to backed-up utility");

            negativeObject=new GameObject("GoScoreBackupNegative");
            var negative=negativeObject.AddComponent<PureUdonGo.GoMctsSearch>();
            negative.simulationState=state;
            Expect(negative.BeginSearch(game,1,32),"negative score search begins");
            Expect(Submit(negative,policy,-19f,5f,-20f),"negative score result accepted");
            float negativeValue=negative.GetRootMeanValue();
            Expect(negativeValue<-0.05f,"negative score contributes with opposite sign");
            Expect(positiveValue>negativeValue,"score direction affects root utility");
            mixedObject=new GameObject("GoScoreBackupNoResultMixture");
            var mixed=mixedObject.AddComponent<PureUdonGo.GoMctsSearch>();
            mixed.simulationState=state;
            Expect(mixed.BeginSearch(game,1,33),"mixed no-result score search begins");
            Expect(Submit(mixed,policy,19f,5f,4f),"mixed no-result score result accepted");
            Expect(mixed.rootNeuralScoreMean>0f&&mixed.rootNeuralScoreMean<19f,
                "no-result probability converts conditional score to an unconditional mean");
            Debug.Log("PURE_UDON_GO_SCORE_BACKUP_METRICS positive="+positiveValue+
                " negative="+negativeValue+" mean="+positive.rootNeuralScoreMean+
                " stdev="+positive.rootNeuralScoreStdev+" mixedMean="+mixed.rootNeuralScoreMean+
                " revision="+positive.rootNeuralOutputRevision);
        }
        catch(Exception exception)
        {
            failures++;
            Debug.LogError("PURE_UDON_GO_SCORE_BACKUP_FAIL exception="+exception);
        }
        finally
        {
            if(mixedObject!=null)UnityEngine.Object.DestroyImmediate(mixedObject);
            if(negativeObject!=null)UnityEngine.Object.DestroyImmediate(negativeObject);
            if(positiveObject!=null)UnityEngine.Object.DestroyImmediate(positiveObject);
            if(stateObject!=null)UnityEngine.Object.DestroyImmediate(stateObject);
            if(gameObject!=null)UnityEngine.Object.DestroyImmediate(gameObject);
            if(failures==0){Debug.Log("PURE_UDON_GO_SCORE_BACKUP_PASS failures=0");EditorApplication.Exit(0);}
            else{Debug.LogError("PURE_UDON_GO_SCORE_BACKUP_FAIL count="+failures);EditorApplication.Exit(6);}
        }
    }
}
#endif
