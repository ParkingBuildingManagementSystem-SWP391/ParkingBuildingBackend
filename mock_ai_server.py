from fastapi import FastAPI, Request
from pydantic import BaseModel
import uvicorn

app = FastAPI()

class AiPredictRequest(BaseModel):
    image_url: str

@app.post("/predict")
async def predict(request: AiPredictRequest):
    print(f"[MOCK AI] Nhan yeu cau nhan dang tu anh: {request.image_url}")
    # Luon tra ve bien so xe test mac dinh de test code logic duoi C#
    return {"license_plate": "30A-99999"}

if __name__ == "__main__":
    print("[MOCK AI] Server bat dau chay tai cong 8000...")
    uvicorn.run(app, host="127.0.0.1", port=8000)
