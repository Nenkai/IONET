
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Quantization;

Image image = Image.Load("s3_waku_dokan01.png");

image.Mutate(x => x.Quantize(new OctreeQuantizer(new QuantizerOptions()
{
    MaxColors = 256,
})));

image.SaveAsPng("new.png");