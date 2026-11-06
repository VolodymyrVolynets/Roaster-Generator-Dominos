import { useEffect, useImperativeHandle, useRef, useState, forwardRef } from 'react'

/** The PDF library ignores transparency, so an unpainted canvas would come out as a black box. */
function paintBackground(canvas) {
  const context = canvas.getContext('2d')
  context.save()
  context.setTransform(1, 0, 0, 1, 0, 0)
  context.fillStyle = '#ffffff'
  context.fillRect(0, 0, canvas.width, canvas.height)
  context.restore()
}

export const SignaturePad = forwardRef(function SignaturePad({ disabled, onChange }, ref) {
  const canvasRef = useRef(null)
  const drawing = useRef(false)
  const [signed, setSigned] = useState(false)

  useImperativeHandle(ref, () => ({
    toBase64: () => canvasRef.current.toDataURL('image/png').split(',')[1],
  }), [])

  useEffect(() => {
    const canvas = canvasRef.current
    const scale = window.devicePixelRatio || 1
    const bounds = canvas.getBoundingClientRect()
    canvas.width = bounds.width * scale
    canvas.height = bounds.height * scale
    const context = canvas.getContext('2d')
    context.scale(scale, scale)
    context.lineWidth = 2
    context.lineCap = 'round'
    context.lineJoin = 'round'
    context.strokeStyle = '#000000'
    paintBackground(canvas)
  }, [])

  const point = (event) => {
    const bounds = canvasRef.current.getBoundingClientRect()
    return [event.clientX - bounds.left, event.clientY - bounds.top]
  }

  function update(value) {
    setSigned(value)
    onChange(value)
  }

  function start(event) {
    event.preventDefault()
    canvasRef.current.setPointerCapture(event.pointerId)
    drawing.current = true
    const context = canvasRef.current.getContext('2d')
    context.beginPath()
    context.moveTo(...point(event))
  }

  function move(event) {
    if (!drawing.current) return
    const context = canvasRef.current.getContext('2d')
    context.lineTo(...point(event))
    context.stroke()
    if (!signed) update(true)
  }

  function clear() {
    paintBackground(canvasRef.current)
    update(false)
  }

  return <div className="document-signature">
    <span>Sign below with your finger or mouse</span>
    <canvas ref={canvasRef} aria-label="Signature area" onPointerDown={start} onPointerMove={move}
      onPointerUp={() => { drawing.current = false }} onPointerCancel={() => { drawing.current = false }} />
    <button type="button" className="secondary-button" onClick={clear} disabled={!signed || disabled}>Clear</button>
  </div>
})
