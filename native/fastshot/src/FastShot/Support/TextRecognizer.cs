namespace FastShot;

/// <summary>Reads text out of a capture with the OCR engine built into Windows (shared with PDF conversion).</summary>
public static class TextRecognizer
{
    public static Task<string> Recognize(BitmapSource image) => Ocr.Recognize(image);
}
