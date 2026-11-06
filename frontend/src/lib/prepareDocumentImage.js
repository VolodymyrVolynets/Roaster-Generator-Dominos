import { fitWithin } from './documents'

const jpegQuality = 0.85

/**
 * Resizes a photo to what the analysis reads and re-encodes it as JPEG, which also
 * removes EXIF data such as the location a phone recorded.
 */
export async function prepareDocumentImage(file) {
  let bitmap
  try {
    bitmap = await createImageBitmap(file, { imageOrientation: 'from-image' })
  } catch {
    throw new Error('This image format is not supported. Upload a JPEG, PNG or PDF instead.')
  }

  const { width, height } = fitWithin(bitmap.width, bitmap.height)
  const canvas = document.createElement('canvas')
  canvas.width = width
  canvas.height = height
  const context = canvas.getContext('2d')
  context.fillStyle = '#ffffff'
  context.fillRect(0, 0, width, height)
  context.drawImage(bitmap, 0, 0, width, height)
  bitmap.close()

  const blob = await new Promise((resolve) => { canvas.toBlob(resolve, 'image/jpeg', jpegQuality) })
  if (!blob) throw new Error('This image could not be prepared. Try another one.')
  return new File([blob], `${file.name.replace(/\.[^.]*$/, '') || 'document'}.jpg`, { type: 'image/jpeg' })
}
