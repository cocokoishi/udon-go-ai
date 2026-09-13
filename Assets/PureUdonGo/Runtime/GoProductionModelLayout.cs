namespace PureUdonGo
{
    /// <summary>
    /// Generated from Model/Generated/packing_manifest.json for the committed
    /// g170-b10c128-s1141046784-d204142634 model. Values are float offsets in
    /// the RGBA32F atlas, not byte offsets.
    /// </summary>
    public static class GoProductionModelLayout
    {
        public const string ModelName="g170-b10c128-s1141046784-d204142634";
        public const int ModelVersion=8;
        public const int WeightWidth=1024;
        public const int WeightHeight=733;
        public const int WeightCapacityFloats=3002368;
        public const float BatchNormEpsilon=0.001f;
        // KataGo V8 post-processing defaults for the four score-value
        // channels in the committed model. The shader keeps raw channels;
        // the controller applies these constants for runtime telemetry.
        public const float ScoreMeanMultiplier=20f;
        public const float ScoreStdevMultiplier=20f;
        public const float LeadMultiplier=20f;

        public const int Conv1=0;
        public const int GInputW=70400;

        public const int Rconv1Norm1Mean=72832;
        public const int Rconv1Norm1Variance=72960;
        public const int Rconv1Norm1Bias=73088;
        public const int Rconv1W1=73216;
        public const int Rconv1Norm2Mean=220672;
        public const int Rconv1Norm2Variance=220800;
        public const int Rconv1Norm2Scale=220928;
        public const int Rconv1Norm2Bias=221056;
        public const int Rconv1W2=221184;

        public const int Rconv2Norm1Mean=368640;
        public const int Rconv2Norm1Variance=368768;
        public const int Rconv2Norm1Bias=368896;
        public const int Rconv2W1=369024;
        public const int Rconv2Norm2Mean=516480;
        public const int Rconv2Norm2Variance=516608;
        public const int Rconv2Norm2Scale=516736;
        public const int Rconv2Norm2Bias=516864;
        public const int Rconv2W2=516992;

        public const int Rconv3Norm1Mean=664448;
        public const int Rconv3Norm1Variance=664576;
        public const int Rconv3Norm1Bias=664704;
        public const int Rconv3W1=664832;
        public const int Rconv3Norm2Mean=812288;
        public const int Rconv3Norm2Variance=812416;
        public const int Rconv3Norm2Scale=812544;
        public const int Rconv3Norm2Bias=812672;
        public const int Rconv3W2=812800;

        public const int Rconv4Norm1Mean=960256;
        public const int Rconv4Norm1Variance=960384;
        public const int Rconv4Norm1Bias=960512;
        public const int Rconv4W1=960640;
        public const int Rconv4Norm2Mean=1108096;
        public const int Rconv4Norm2Variance=1108224;
        public const int Rconv4Norm2Scale=1108352;
        public const int Rconv4Norm2Bias=1108480;
        public const int Rconv4W2=1108608;

        public const int Rconv5Norm1Mean=1256064;
        public const int Rconv5Norm1Variance=1256192;
        public const int Rconv5Norm1Bias=1256320;
        public const int Rconv5W1A=1256448;
        public const int Rconv5W1B=1367040;
        public const int Rconv5Norm1BMean=1403904;
        public const int Rconv5Norm1BVariance=1403936;
        public const int Rconv5Norm1BBias=1403968;
        public const int Rconv5W1R=1404000;
        public const int Rconv5Norm2Mean=1413216;
        public const int Rconv5Norm2Variance=1413312;
        public const int Rconv5Norm2Scale=1413408;
        public const int Rconv5Norm2Bias=1413504;
        public const int Rconv5W2=1413600;

        public const int Rconv6Norm1Mean=1524192;
        public const int Rconv6Norm1Variance=1524320;
        public const int Rconv6Norm1Bias=1524448;
        public const int Rconv6W1=1524576;
        public const int Rconv6Norm2Mean=1672032;
        public const int Rconv6Norm2Variance=1672160;
        public const int Rconv6Norm2Scale=1672288;
        public const int Rconv6Norm2Bias=1672416;
        public const int Rconv6W2=1672544;

        public const int Rconv7Norm1Mean=1820000;
        public const int Rconv7Norm1Variance=1820128;
        public const int Rconv7Norm1Bias=1820256;
        public const int Rconv7W1=1820384;
        public const int Rconv7Norm2Mean=1967840;
        public const int Rconv7Norm2Variance=1967968;
        public const int Rconv7Norm2Scale=1968096;
        public const int Rconv7Norm2Bias=1968224;
        public const int Rconv7W2=1968352;

        public const int Rconv8Norm1Mean=2115808;
        public const int Rconv8Norm1Variance=2115936;
        public const int Rconv8Norm1Bias=2116064;
        public const int Rconv8W1A=2116192;
        public const int Rconv8W1B=2226784;
        public const int Rconv8Norm1BMean=2263648;
        public const int Rconv8Norm1BVariance=2263680;
        public const int Rconv8Norm1BBias=2263712;
        public const int Rconv8W1R=2263744;
        public const int Rconv8Norm2Mean=2272960;
        public const int Rconv8Norm2Variance=2273056;
        public const int Rconv8Norm2Scale=2273152;
        public const int Rconv8Norm2Bias=2273248;
        public const int Rconv8W2=2273344;

        public const int Rconv9Norm1Mean=2383936;
        public const int Rconv9Norm1Variance=2384064;
        public const int Rconv9Norm1Bias=2384192;
        public const int Rconv9W1=2384320;
        public const int Rconv9Norm2Mean=2531776;
        public const int Rconv9Norm2Variance=2531904;
        public const int Rconv9Norm2Scale=2532032;
        public const int Rconv9Norm2Bias=2532160;
        public const int Rconv9W2=2532288;

        public const int Rconv10Norm1Mean=2679744;
        public const int Rconv10Norm1Variance=2679872;
        public const int Rconv10Norm1Bias=2680000;
        public const int Rconv10W1=2680128;
        public const int Rconv10Norm2Mean=2827584;
        public const int Rconv10Norm2Variance=2827712;
        public const int Rconv10Norm2Scale=2827840;
        public const int Rconv10Norm2Bias=2827968;
        public const int Rconv10W2=2828096;

        public const int TrunkNormMean=2975552;
        public const int TrunkNormVariance=2975680;
        public const int TrunkNormBias=2975808;

        public const int PolicyP1W=2975936;
        public const int PolicyG1W=2980032;
        public const int PolicyG1NormMean=2984128;
        public const int PolicyG1NormVariance=2984160;
        public const int PolicyG1NormBias=2984192;
        public const int PolicyG2W=2984224;
        public const int PolicyP1NormMean=2987296;
        public const int PolicyP1NormVariance=2987328;
        public const int PolicyP1NormBias=2987360;
        public const int PolicyP2W=2987392;
        public const int PolicyPassW=2987424;

        public const int ValueV1W=2987520;
        public const int ValueV1NormMean=2991616;
        public const int ValueV1NormVariance=2991648;
        public const int ValueV1NormBias=2991680;
        public const int ValueV2W=2991712;
        public const int ValueV2B=2999392;
        public const int ValueV3W=2999472;
        public const int ValueV3B=2999712;
        public const int ScoreV3W=2999715;
        public const int ScoreV3B=3000035;
        public const int OwnershipW=3000039;
    }
}
