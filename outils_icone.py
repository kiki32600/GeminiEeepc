import numpy as np
from PIL import Image
N=1024
y,x=np.mgrid[0:N,0:N]
u=(x-N/2+0.5)/(N*0.48); v=(y-N/2+0.5)/(N*0.48)
p=0.62
d=np.abs(u)**p+np.abs(v)**p
# antialias via smooth edge
alpha=np.clip((1-d)/0.012,0,1)
# gradient: angle-based blend of 4 colors
t=np.clip((u - v)/2*0.5+0.5,0,1)  # bottom-left->top-right
c1=np.array([40,200,170]); c2=np.array([45,120,240]); c3=np.array([150,90,235])
col=np.where(t[...,None]<0.5, c1+(c2-c1)*(t[...,None]/0.5), c2+(c3-c2)*((t[...,None]-0.5)/0.5))
# soft highlight near center
h=np.exp(-(u**2+v**2)/0.08)[...,None]
col=col*(1-0.35*h)+255*0.35*h
img=np.dstack([col,alpha*255]).astype(np.uint8)
im=Image.fromarray(img,'RGBA')
im.resize((256,256),Image.LANCZOS).save('logo.png')
im.resize((256,256),Image.LANCZOS).save('app.ico',sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
im.resize((512,512),Image.LANCZOS).save('apercu.png')
